using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using ManagedBass.Mix;

public sealed class AudioEngine : IDisposable
{
    private readonly int _mixer;
    private int _musicStream; 
    private bool _isDisposed;
    // A YM file is played by our own YM2149 through a stream that asks for the sound as it goes
    private AmosLikeBasic.YmPlayer? _ymPlayer;
    private StreamProcedure? _ymProcedure;
    private float[] _ymBuffer = Array.Empty<float>();

    private readonly Dictionary<string, int> _activeSamples = new(StringComparer.OrdinalIgnoreCase);

    
    public AudioEngine()
    {
        Bass.Configure(Configuration.PlaybackBufferLength, 200);
        
        // The sound card and channels chosen in View → Sound Output
        if (!AmosLikeBasic.AudioOutput.Start())
        {
            throw new Exception($"Could not initialise BASS: {Bass.LastError}");
        }

        _mixer = BassMix.CreateMixerStream(44100, 2, BassFlags.Default | BassFlags.Float | AmosLikeBasic.AudioOutput.PairFlags);
        Bass.ChannelPlay(_mixer);
    }

    public void PlayMod(string filePath)
    {
        string fullPath = ResourceLoader.GetPath(filePath);
            
        // Read the file into memory in C# first (works around file system problems in BASS on the Mac)
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"File missing: {fullPath}");
            return;
        }

        // Clean up old music
        if (_musicStream != 0)
        {
            BassMix.MixerRemoveChannel(_musicStream);
            Bass.StreamFree(_musicStream);
            _musicStream = 0;
        }

        try 
        {
            byte[] data = File.ReadAllBytes(fullPath);
            AmosLikeBasic.AudioOutput.UseDevice();

            if (AmosLikeBasic.YmFile.IsChipMusic(data))
            {
                PlayYm(data, fullPath);
                return;
            }

            // Use the overload that takes a byte array (data) instead of a file name
            // Parametrar: Data, Offset, Length, Flags, Frequency (0 = default)
            _musicStream = Bass.MusicLoad(data, 0, data.Length, BassFlags.MusicRamp | BassFlags.Decode | BassFlags.Float | BassFlags.Prescan, 0);
            
            if (_musicStream != 0)
            {
                // Add to the mixer
                bool added = BassMix.MixerAddChannel(_mixer, _musicStream, BassFlags.Default | (BassFlags)0x2000);
            
                if (added)
                {
                    Bass.ChannelSetPosition(_musicStream, 0);
                    Console.WriteLine($"Nu spelas: {fullPath}");
                }
                else
                {
                    Console.WriteLine($"Mixer error: {Bass.LastError}");
                }
            }
            else
            {
                Console.WriteLine($"Could not load the MOD file ({Bass.LastError})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while loading the MOD: {ex.Message}");
        }
    }

        public void PlaySample(string filePath, bool loop = false)
        {
            string fullPath = ResourceLoader.GetPath(filePath);
            if (!File.Exists(fullPath)) return;

            // Stop any old instance of the same sample first
            StopSample(fullPath);
        
            // Add the loop flag if wanted
            var flags = BassFlags.Default | AmosLikeBasic.AudioOutput.PairFlags;
            if (loop) flags |= BassFlags.Loop;
        
            // We create an ordinary stream that does NOT go through the mixer.
            // Without BassFlags.Decode it plays straight on the sound card.
            // CHANGED HERE: Uses the variable 'flags' instead of BassFlags.Default
            AmosLikeBasic.AudioOutput.UseDevice();
            int effectStream = Bass.CreateStream(fullPath, 0, 0, flags);
        
            if (effectStream != 0)
            {
                // Keep the handle so we can stop it later
                _activeSamples[fullPath] = effectStream;
            
                // Set the volume a bit higher for effects if needed
                Bass.ChannelSetAttribute(effectStream, ChannelAttribute.Volume, 1.0);
            
                // Set a callback that removes it from the list when it has finished (SyncEnd)
                // NOTE: If it loops, SyncEnd never runs, so we avoid removing it too early.
                if (!loop)
                {
                    string capturedPath = fullPath;
                    int capturedHandle = effectStream;
                    
                    Bass.ChannelSetSync(effectStream, SyncFlags.End, 0, (handle, channel, data, user) => 
                    {
                        if (!_isDisposed)
                        {
                            lock(_activeSamples) 
                            {
                                if (_activeSamples.TryGetValue(capturedPath, out int existingHandle) && 
                                    existingHandle == capturedHandle)
                                {
                                    _activeSamples.Remove(capturedPath);
                                }
                            }
                        }
                    });
                }
            
                // Play straight away! This bypasses the mixer and has minimal latency.
                Bass.ChannelPlay(effectStream);
            }
            else
            {
                Console.WriteLine($"BASS Error {Bass.LastError} vid laddning av: {filePath}");
            }
        }
    
    public void StopSample(string filePath)
    {
        string fullPath = ResourceLoader.GetPath(filePath);
        lock (_activeSamples)
        {
            if (_activeSamples.TryGetValue(fullPath, out int handle))
            {
                Bass.ChannelStop(handle);
                Bass.StreamFree(handle);
                _activeSamples.Remove(fullPath);
            }
        }
    }
    
    public void StopAllSamples()
    {
        lock (_activeSamples)
        {
            foreach (var handle in _activeSamples.Values)
            {
                Bass.ChannelStop(handle);
                Bass.StreamFree(handle);
            }
            _activeSamples.Clear();
        }
    }


    public void StopMod()
    {
        if (_musicStream != 0)
        {
            Bass.ChannelStop(_musicStream);
            Bass.StreamFree(_musicStream);
            _musicStream = 0;
        }
        _ymPlayer = null;
    }

    private void PlayYm(byte[] data, string fullPath)
    {
        AmosLikeBasic.YmFile ym;
        try { ym = AmosLikeBasic.YmFile.ReadChipMusic(data); }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"Could not read the YM file: {ex.Message}");
            return;
        }
        var player = new AmosLikeBasic.YmPlayer(ym, 44100);
        _ymPlayer = player;
        // Kept in a field, so that the garbage collector does not take it while BASS calls it
        _ymProcedure = (handle, buffer, length, user) =>
        {
            int frames = length / 8;   // stereo float
            if (_ymBuffer.Length < frames) _ymBuffer = new float[frames];
            var mono = _ymBuffer.AsSpan(0, frames);
            player.Render(mono);
            unsafe
            {
                var dest = new Span<float>((void*)buffer, frames * 2);
                for (int i = 0; i < frames; i++) dest[i * 2] = dest[i * 2 + 1] = mono[i];
            }
            return player.Ended ? (frames * 8) | unchecked((int)StreamProcedureType.End) : frames * 8;
        };
        _musicStream = Bass.CreateStream(44100, 2, BassFlags.Decode | BassFlags.Float, _ymProcedure);
        if (_musicStream != 0 && BassMix.MixerAddChannel(_mixer, _musicStream, BassFlags.Default | (BassFlags)0x2000))
            Console.WriteLine($"Nu spelas: {fullPath} ({ym.Title}{(ym.Author.Length > 0 ? " by " + ym.Author : "")})");
        else
            Console.WriteLine($"Could not play the YM file ({Bass.LastError})");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        StopAllSamples(); // Stop all sound effects
        StopMod();        // Stop the module
        Bass.StreamFree(_mixer);
        Bass.Free();
        _isDisposed = true;
    }
}