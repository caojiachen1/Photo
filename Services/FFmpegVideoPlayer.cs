using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using NAudio.Wave;
using Windows.Storage.Streams;

namespace Photo.Services
{
    public unsafe class FFmpegVideoPlayer : IDisposable
    {
        private AVFormatContext* _formatContext;
        private AVCodecContext* _videoCodecContext;
        private AVCodecContext* _audioCodecContext;
        private AVFrame* _frame;
        private AVFrame* _frameRGB;
        private AVFrame* _audioFrame;
        private SwsContext* _swsContext;
        private SwrContext* _swrContext;
        private int _videoStreamIndex = -1;
        private int _audioStreamIndex = -1;
        private byte[]? _videoBuffer;
        private bool _isPlaying;
        private bool _isPaused;
        private Thread? _playbackThread;
        private CancellationTokenSource? _cts;
        private long _duration;
        private long _currentPts;
        private DispatcherQueue _dispatcherQueue;
        private float _volume = 1.0f;
        private bool _isMuted = false;
        
        // Audio playback
        private BufferedWaveProvider? _waveProvider;
        private WaveOutEvent? _waveOut;
        private readonly object _seekLock = new object();
        private bool _seekRequested = false;
        private double _seekTarget = 0;
        
        // Performance optimization - reusable bitmap
        private WriteableBitmap? _reusableBitmap;
        private readonly object _bitmapLock = new object();
        
        // Performance optimization for large files - packet queue
        private System.Collections.Concurrent.ConcurrentQueue<IntPtr>? _videoPacketQueue;
        private System.Collections.Concurrent.ConcurrentQueue<IntPtr>? _audioPacketQueue;
        private Thread? _demuxThread;
        private const int MaxPacketQueueSize = 120; // 大文件缓冲更多帧
        private bool _isLargeFile = false;
        private const long LargeFileSizeThreshold = 100 * 1024 * 1024; // 100MB
        private const long LargeFileDurationThreshold = 600; // 10分钟
        
        // Thread safety locks
        private readonly object _ffmpegLock = new object();
        private readonly object _bufferLock = new object();
        
        public event EventHandler<WriteableBitmap>? FrameReady;
        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackPaused;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<long>? PositionChanged;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double Duration => _duration / (double)ffmpeg.AV_TIME_BASE;
        public double Position => _videoStreamIndex >= 0 && _formatContext != null 
            ? _currentPts * ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base) 
            : 0;
        public bool IsPlaying => _isPlaying && !_isPaused;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                if (_waveOut != null)
                {
                    _waveOut.Volume = _isMuted ? 0 : _volume;
                }
            }
        }
        public float Volume 
        { 
            get => _volume; 
            set 
            {
                _volume = Math.Clamp(value, 0f, 1f);
                if (_waveOut != null && !_isMuted)
                {
                    _waveOut.Volume = _volume;
                }
            }
        }
        
        public bool IsLargeFile => _isLargeFile;
        public double FrameRate { get; private set; }

        public FFmpegVideoPlayer(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        static FFmpegVideoPlayer()
        {
            // 设置 FFmpeg 库路径
            var ffmpegPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin");
            ffmpeg.RootPath = ffmpegPath;
            
            // 注册所有格式和编解码器
            // FFmpeg 6.0+ 不再需要调用 av_register_all()
        }

        public bool Open(string filePath)
        {
            Close();

            try
            {
                fixed (AVFormatContext** formatContext = &_formatContext)
                {
                    var result = ffmpeg.avformat_open_input(formatContext, filePath, null, null);
                    if (result != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"无法打开文件: {filePath}, 错误码: {result}");
                        return false;
                    }
                }

                if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
                {
                    System.Diagnostics.Debug.WriteLine("无法找到流信息");
                    return false;
                }

                // 找到视频流和音频流
                for (int i = 0; i < _formatContext->nb_streams; i++)
                {
                    var streamType = _formatContext->streams[i]->codecpar->codec_type;
                    if (streamType == AVMediaType.AVMEDIA_TYPE_VIDEO && _videoStreamIndex == -1)
                    {
                        _videoStreamIndex = i;
                    }
                    else if (streamType == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex == -1)
                    {
                        _audioStreamIndex = i;
                    }
                }

                if (_videoStreamIndex == -1)
                {
                    System.Diagnostics.Debug.WriteLine("未找到视频流");
                    return false;
                }

                // 初始化视频解码器
                var videoCodecParameters = _formatContext->streams[_videoStreamIndex]->codecpar;
                var videoCodec = ffmpeg.avcodec_find_decoder(videoCodecParameters->codec_id);
                if (videoCodec == null)
                {
                    System.Diagnostics.Debug.WriteLine($"无法找到视频解码器: {videoCodecParameters->codec_id}");
                    return false;
                }

                _videoCodecContext = ffmpeg.avcodec_alloc_context3(videoCodec);
                if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, videoCodecParameters) < 0)
                    return false;

                if (ffmpeg.avcodec_open2(_videoCodecContext, videoCodec, null) < 0)
                    return false;

                Width = _videoCodecContext->width;
                Height = _videoCodecContext->height;
                _duration = _formatContext->duration;
                
                // 计算帧率
                var frameRateRational = _formatContext->streams[_videoStreamIndex]->avg_frame_rate;
                FrameRate = frameRateRational.den > 0 ? ffmpeg.av_q2d(frameRateRational) : 30.0;
                
                // 检测是否为大文件（基于大小或时长）
                var fileInfo = new System.IO.FileInfo(filePath);
                _isLargeFile = fileInfo.Length > LargeFileSizeThreshold || Duration > LargeFileDurationThreshold;

                _frame = ffmpeg.av_frame_alloc();
                _frameRGB = ffmpeg.av_frame_alloc();

                var numBytes = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, Width, Height, 1);
                _videoBuffer = new byte[numBytes];

                fixed (byte* bufferPtr = _videoBuffer)
                {
                    var data = new byte_ptrArray4();
                    var linesize = new int_array4();
                    
                    ffmpeg.av_image_fill_arrays(
                        ref data,
                        ref linesize,
                        bufferPtr,
                        AVPixelFormat.AV_PIX_FMT_BGRA, Width, Height, 1);
                        
                    for (uint i = 0; i < 4; i++)
                    {
                        _frameRGB->data[i] = data[i];
                        _frameRGB->linesize[i] = linesize[i];
                    }
                }

                _swsContext = ffmpeg.sws_getContext(
                    Width, Height, _videoCodecContext->pix_fmt,
                    Width, Height, AVPixelFormat.AV_PIX_FMT_BGRA,
                    _isLargeFile ? 4 : 2, null, null, null); // SWS_FAST_BILINEAR=1, SWS_BILINEAR=2, SWS_BICUBIC=4

                // 初始化音频解码器
                if (_audioStreamIndex >= 0)
                {
                    InitializeAudio();
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打开视频文件时发生异常: {ex.Message}");
                return false;
            }
        }

        private void InitializeAudio()
        {
            try
            {
                var audioCodecParameters = _formatContext->streams[_audioStreamIndex]->codecpar;
                var audioCodec = ffmpeg.avcodec_find_decoder(audioCodecParameters->codec_id);
                if (audioCodec == null)
                {
                    System.Diagnostics.Debug.WriteLine("无法找到音频解码器");
                    _audioStreamIndex = -1;
                    return;
                }

                _audioCodecContext = ffmpeg.avcodec_alloc_context3(audioCodec);
                if (ffmpeg.avcodec_parameters_to_context(_audioCodecContext, audioCodecParameters) < 0)
                {
                    _audioStreamIndex = -1;
                    return;
                }

                if (ffmpeg.avcodec_open2(_audioCodecContext, audioCodec, null) < 0)
                {
                    _audioStreamIndex = -1;
                    return;
                }

                _audioFrame = ffmpeg.av_frame_alloc();

                // 初始化重采样器 - 转换为 16-bit PCM stereo 44100Hz
                _swrContext = ffmpeg.swr_alloc();
                
                var inChannelLayout = _audioCodecContext->ch_layout;
                AVChannelLayout outChannelLayout;
                ffmpeg.av_channel_layout_default(&outChannelLayout, 2); // stereo
                
                fixed (SwrContext** swrCtxPtr = &_swrContext)
                {
                    ffmpeg.swr_alloc_set_opts2(
                        swrCtxPtr,
                        &outChannelLayout,
                        AVSampleFormat.AV_SAMPLE_FMT_S16,
                        44100,
                        &inChannelLayout,
                        _audioCodecContext->sample_fmt,
                        _audioCodecContext->sample_rate,
                        0, null);
                }

                if (ffmpeg.swr_init(_swrContext) < 0)
                {
                    System.Diagnostics.Debug.WriteLine("无法初始化音频重采样器");
                    _audioStreamIndex = -1;
                    return;
                }

                // 初始化 NAudio 播放器
                _waveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2))
                {
                    BufferDuration = TimeSpan.FromSeconds(_isLargeFile ? 10 : 5), // 大文件更大的缓冲
                    DiscardOnBufferOverflow = true
                };

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveProvider);
                _waveOut.Volume = _volume;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化音频失败: {ex.Message}");
                _audioStreamIndex = -1;
            }
        }

        public void Play()
        {
            if (_isPlaying && !_isPaused)
                return;

            if (_isPaused)
            {
                _isPaused = false;
                _waveOut?.Play();
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                return;
            }

            _isPlaying = true;
            _isPaused = false;
            _cts = new CancellationTokenSource();

            _waveOut?.Play();
            
            // 大文件使用分离的解复用和解码线程
            if (_isLargeFile)
            {
                _videoPacketQueue = new System.Collections.Concurrent.ConcurrentQueue<IntPtr>();
                _audioPacketQueue = new System.Collections.Concurrent.ConcurrentQueue<IntPtr>();
                
                _demuxThread = new Thread(() => DemuxLoop(_cts.Token))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _demuxThread.Start();
            }

            _playbackThread = new Thread(() => PlaybackLoop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _playbackThread.Start();

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (!_isPlaying || _isPaused)
                return;

            _isPaused = true;
            _waveOut?.Pause();
            PlaybackPaused?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _isPlaying = false;
            _isPaused = false;
            _cts?.Cancel();
            _waveOut?.Stop();
            _demuxThread?.Join(1000);
            _playbackThread?.Join(1000);
            ClearPacketQueues();
        }

        public void Seek(double positionSeconds)
        {
            if (_formatContext == null || _videoStreamIndex < 0)
                return;

            lock (_seekLock)
            {
                _seekRequested = true;
                _seekTarget = positionSeconds;
            }
        }

        private void PerformSeek(double positionSeconds)
        {
            // 清空 packet 队列
            ClearPacketQueues();
            
            lock (_ffmpegLock)
            {
                if (_formatContext == null) return;

                // 使用视频流的时间基准进行更精确的 seek
                var videoStream = _formatContext->streams[_videoStreamIndex];
                var timestamp = (long)(positionSeconds / ffmpeg.av_q2d(videoStream->time_base));
                
                // 对于短视频使用更精确的 seek 标志
                int seekFlags = ffmpeg.AVSEEK_FLAG_BACKWARD;
                if (Duration < 60) // 短视频使用帧级精度
                {
                    seekFlags |= ffmpeg.AVSEEK_FLAG_FRAME;
                }
                
                // 尝试精确 seek
                var result = ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, timestamp, seekFlags);
                if (result < 0 && (seekFlags & ffmpeg.AVSEEK_FLAG_FRAME) != 0)
                {
                    // 帧级精度失败，尝试普通 seek
                    ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
                }
                
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);
                if (_audioCodecContext != null)
                {
                    ffmpeg.avcodec_flush_buffers(_audioCodecContext);
                }
            }
            _waveProvider?.ClearBuffer();
            
            // 更新当前位置
            // 注意：这里只是近似，准确位置会在读取下一帧时更新
            // _currentPts = timestamp; 
        }
        
        private void ClearPacketQueues()
        {
            if (_videoPacketQueue != null)
            {
                while (_videoPacketQueue.TryDequeue(out var packetPtr))
                {
                    var packet = (AVPacket*)packetPtr;
                    ffmpeg.av_packet_unref(packet);
                    ffmpeg.av_packet_free(&packet);
                }
            }
            if (_audioPacketQueue != null)
            {
                while (_audioPacketQueue.TryDequeue(out var packetPtr))
                {
                    var packet = (AVPacket*)packetPtr;
                    ffmpeg.av_packet_unref(packet);
                    ffmpeg.av_packet_free(&packet);
                }
            }
        }
        
        private void DemuxLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isPlaying)
                {
                    // 检查 seek 请求
                    lock (_seekLock)
                    {
                        if (_seekRequested)
                        {
                            Thread.Sleep(10); // 等待主线程处理
                            continue;
                        }
                    }
                    
                    if (_isPaused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    
                    // 控制队列大小，避免内存过大
                    if ((_videoPacketQueue?.Count ?? 0) >= MaxPacketQueueSize)
                    {
                        Thread.Sleep(5);
                        continue;
                    }
                    
                    var packet = ffmpeg.av_packet_alloc();
                    int readResult;
                    
                    lock (_ffmpegLock)
                    {
                        if (_formatContext == null) break;
                        readResult = ffmpeg.av_read_frame(_formatContext, packet);
                    }
                    
                    if (readResult < 0)
                    {
                        ffmpeg.av_packet_free(&packet);
                        // 播放结束
                        break;
                    }
                    
                    if (packet->stream_index == _videoStreamIndex)
                    {
                        _videoPacketQueue?.Enqueue((IntPtr)packet);
                    }
                    else if (packet->stream_index == _audioStreamIndex && _audioCodecContext != null)
                    {
                        _audioPacketQueue?.Enqueue((IntPtr)packet);
                    }
                    else
                    {
                        ffmpeg.av_packet_unref(packet);
                        ffmpeg.av_packet_free(&packet);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解复用线程异常: {ex.Message}");
            }
        }

        private void PlaybackLoop(CancellationToken cancellationToken)
        {
            var packet = ffmpeg.av_packet_alloc();
            var timeBase = ffmpeg.av_q2d(_formatContext->streams[_videoStreamIndex]->time_base);
            var startTime = DateTime.Now;
            var firstPts = -1L;
            var usePacketQueue = _isLargeFile && _videoPacketQueue != null;

            try
            {
                while (!cancellationToken.IsCancellationRequested && _isPlaying)
                {
                    // Check for seek request
                    lock (_seekLock)
                    {
                        if (_seekRequested)
                        {
                            PerformSeek(_seekTarget);
                            _seekRequested = false;
                            firstPts = -1;
                            startTime = DateTime.Now;
                        }
                    }

                    if (_isPaused)
                    {
                        Thread.Sleep(10);
                        startTime = DateTime.Now;
                        if (firstPts >= 0)
                            firstPts = _currentPts;
                        continue;
                    }
                    
                    AVPacket* currentPacket;
                    bool packetObtained = false;
                    
                    if (usePacketQueue)
                    {
                        // 大文件模式：从队列获取 packet
                        if (_videoPacketQueue!.TryDequeue(out var packetPtr))
                        {
                            currentPacket = (AVPacket*)packetPtr;
                            packetObtained = true;
                        }
                        else
                        {
                            // 队列为空，等待解复用线程
                            Thread.Sleep(1);
                            continue;
                        }
                        
                        // 同时处理音频队列
                        while (_audioPacketQueue != null && _audioPacketQueue.TryDequeue(out var audioPacketPtr))
                        {
                            var audioPacket = (AVPacket*)audioPacketPtr;
                            ProcessAudioPacket(audioPacket);
                            ffmpeg.av_packet_unref(audioPacket);
                            ffmpeg.av_packet_free(&audioPacket);
                        }
                    }
                    else
                    {
                        // 小文件模式：直接读取
                        int readResult;
                        lock (_ffmpegLock)
                        {
                            if (_formatContext == null)
                            {
                                readResult = -1;
                            }
                            else
                            {
                                readResult = ffmpeg.av_read_frame(_formatContext, packet);
                            }
                        }

                        if (readResult < 0)
                        {
                            // 播放结束
                            _isPlaying = false;
                            _waveOut?.Stop();
                            PlaybackEnded?.Invoke(this, EventArgs.Empty);
                            break;
                        }
                        currentPacket = packet;
                        packetObtained = true;
                    }
                    
                    if (!packetObtained)
                        continue;

                    if (currentPacket->stream_index == _videoStreamIndex)
                    {
                        ProcessVideoPacket(currentPacket, ref firstPts, ref startTime, timeBase);
                    }
                    else if (!usePacketQueue && currentPacket->stream_index == _audioStreamIndex && _audioCodecContext != null)
                    {
                        ProcessAudioPacket(currentPacket);
                    }

                    if (usePacketQueue)
                    {
                        ffmpeg.av_packet_unref(currentPacket);
                        ffmpeg.av_packet_free(&currentPacket);
                    }
                    else
                    {
                        ffmpeg.av_packet_unref(packet);
                    }
                }
                
                // 大文件模式：检查是否播放完成
                if (usePacketQueue && _isPlaying && !cancellationToken.IsCancellationRequested)
                {
                    _isPlaying = false;
                    _waveOut?.Stop();
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                ffmpeg.av_packet_free(&packet);
            }
        }

        private void ProcessVideoPacket(AVPacket* packet, ref long firstPts, ref DateTime startTime, double timeBase)
        {
            if (ffmpeg.avcodec_send_packet(_videoCodecContext, packet) == 0)
            {
                while (ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame) == 0)
                {
                    _currentPts = _frame->pts;
                    
                    if (firstPts < 0)
                        firstPts = _currentPts;

                    // 计算帧的显示时间
                    var framePts = (_currentPts - firstPts) * timeBase;
                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    var delay = framePts - elapsed;

                    if (delay > 0 && delay < 1)
                        Thread.Sleep((int)(delay * 1000));

                    // 转换为 BGRA
                    lock (_bufferLock)
                    {
                        ffmpeg.sws_scale(_swsContext, _frame->data, _frame->linesize, 0, Height,
                            _frameRGB->data, _frameRGB->linesize);
                    }

                    // 在 UI 线程上更新 WriteableBitmap（复用以提高性能）
                    var buffer = _videoBuffer;
                    var width = Width;
                    var height = Height;
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        lock (_bitmapLock)
                        {
                            if (_reusableBitmap == null || _reusableBitmap.PixelWidth != width || _reusableBitmap.PixelHeight != height)
                            {
                                _reusableBitmap = new WriteableBitmap(width, height);
                            }
                            
                            lock (_bufferLock)
                            {
                                buffer.AsBuffer().CopyTo(_reusableBitmap.PixelBuffer);
                            }
                            
                            _reusableBitmap.Invalidate();
                            FrameReady?.Invoke(this, _reusableBitmap);
                        }
                    });
                    
                    PositionChanged?.Invoke(this, _currentPts);
                }
            }
        }

        private void ProcessAudioPacket(AVPacket* packet)
        {
            if (ffmpeg.avcodec_send_packet(_audioCodecContext, packet) == 0)
            {
                while (ffmpeg.avcodec_receive_frame(_audioCodecContext, _audioFrame) == 0)
                {
                    // 计算输出采样数
                    var outSamples = (int)ffmpeg.av_rescale_rnd(
                        ffmpeg.swr_get_delay(_swrContext, _audioCodecContext->sample_rate) + _audioFrame->nb_samples,
                        44100,
                        _audioCodecContext->sample_rate,
                        AVRounding.AV_ROUND_UP);

                    // 分配输出缓冲区
                    byte* outBuffer;
                    ffmpeg.av_samples_alloc(&outBuffer, null, 2, outSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 0);

                    // 重采样
                    var convertedSamples = ffmpeg.swr_convert(
                        _swrContext,
                        &outBuffer,
                        outSamples,
                        _audioFrame->extended_data,
                        _audioFrame->nb_samples);

                    if (convertedSamples > 0)
                    {
                        var dataSize = convertedSamples * 2 * 2; // stereo, 16-bit
                        var audioData = new byte[dataSize];
                        Marshal.Copy((IntPtr)outBuffer, audioData, 0, dataSize);
                        _waveProvider?.AddSamples(audioData, 0, dataSize);
                    }

                    ffmpeg.av_freep(&outBuffer);
                }
            }
        }

        public void Close()
        {
            Stop();

            _waveOut?.Dispose();
            _waveOut = null;
            _waveProvider = null;
            
            lock (_bitmapLock)
            {
                _reusableBitmap = null;
            }

            if (_swrContext != null)
            {
                fixed (SwrContext** ctx = &_swrContext)
                {
                    ffmpeg.swr_free(ctx);
                }
                _swrContext = null;
            }

            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }

            if (_audioFrame != null)
            {
                var frame = _audioFrame;
                ffmpeg.av_frame_free(&frame);
                _audioFrame = null;
            }

            if (_frameRGB != null)
            {
                var frame = _frameRGB;
                ffmpeg.av_frame_free(&frame);
                _frameRGB = null;
            }

            if (_frame != null)
            {
                var frame = _frame;
                ffmpeg.av_frame_free(&frame);
                _frame = null;
            }

            if (_audioCodecContext != null)
            {
                var codecContext = _audioCodecContext;
                ffmpeg.avcodec_free_context(&codecContext);
                _audioCodecContext = null;
            }

            if (_videoCodecContext != null)
            {
                var codecContext = _videoCodecContext;
                ffmpeg.avcodec_free_context(&codecContext);
                _videoCodecContext = null;
            }

            if (_formatContext != null)
            {
                lock (_ffmpegLock)
                {
                    var formatContext = _formatContext;
                    ffmpeg.avformat_close_input(&formatContext);
                    _formatContext = null;
                }
            }

            _videoStreamIndex = -1;
            _audioStreamIndex = -1;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
