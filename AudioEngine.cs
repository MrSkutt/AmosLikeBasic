using System;
using System.Runtime.InteropServices; 
using System.Collections.Generic;
using System.Threading;
using OpenTK.Audio.OpenAL;

public sealed class AudioEngine : IDisposable
{
    private readonly ALDevice _device;
    private readonly ALContext _context;

    private readonly int _source;
    private readonly Queue<int> _buffers = new();

    private Thread? _thread;
    private bool _running;

    private const int SampleRate = 44100;
    private const int FramesPerChunk = 1024;

    public AudioEngine()
    {
        _device = ALC.OpenDevice(null);
        if (_device == ALDevice.Null)
            throw new Exception("Failed to open OpenAL device");

        _context = ALC.CreateContext(_device, (int[])null!);
        ALC.MakeContextCurrent(_context);

        _source = AL.GenSource();

        for (int i = 0; i < 4; i++)
            _buffers.Enqueue(AL.GenBuffer());
    }

    public void PlayMod(IntPtr xmpContext)
    {
        _running = true;
        _thread = new Thread(() =>
        {
            byte[] pcm = new byte[FramesPerChunk * 4]; // 16-bit stereo = 4 bytes per frame
            GCHandle handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);

            while (_running)
            {
                int processed;
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out processed);
                while (processed-- > 0)
                {
                    int buf = AL.SourceUnqueueBuffer(_source);
                    _buffers.Enqueue(buf);
                }

                if (_buffers.Count == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // Be LibXmp fylla vår buffer med ljuddata
                int ret = LibXmp.xmp_play_buffer(xmpContext, handle.AddrOfPinnedObject(), pcm.Length, 0);
                if (ret != 0) break; // Slut på låten

                int buffer = _buffers.Dequeue();

                // Skicka bytedatan till OpenAL
                AL.BufferData(buffer, ALFormat.Stereo16, pcm, SampleRate);
                AL.SourceQueueBuffer(_source, buffer);

                AL.GetSource(_source, ALGetSourcei.SourceState, out int stateInt);
                if ((ALSourceState)stateInt != ALSourceState.Playing)
                    AL.SourcePlay(_source);
            }

            handle.Free();
        });
        _thread.IsBackground = true;
        _thread.Start();
    }
    
    
    public void StopMod()
    {
        //_isPlaying = false;
        //_xmpContext = IntPtr.Zero;
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join();

        AL.SourceStop(_source);

        foreach (var b in _buffers)
            AL.DeleteBuffer(b);

        AL.DeleteSource(_source);

        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(_context);
    }
}
   