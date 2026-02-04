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

    private readonly Dictionary<string, int> _activeSamples = new(StringComparer.OrdinalIgnoreCase);

    
    public AudioEngine()
    {
        Bass.Configure(Configuration.PlaybackBufferLength, 200);
        
        if (!Bass.Init(-1, 44100))
        {
            throw new Exception($"Kunde inte initiera BASS: {Bass.LastError}");
        }

        _mixer = BassMix.CreateMixerStream(44100, 2, BassFlags.Default | BassFlags.Float);
        Bass.ChannelPlay(_mixer);
    }

    public void PlayMod(string filePath)
    {
        string fullPath = ResourceLoader.GetPath(filePath);
        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"Fil saknas: {filePath}");
            return;
        }

        if (_musicStream != 0)
        {
            BassMix.MixerRemoveChannel(_musicStream);
            Bass.StreamFree(_musicStream);
        }

        // Vi laddar MOD-filen med standardinställningar + Decode
        // MusicRamp tar bort "knäppar" i ljudet.
        _musicStream = Bass.MusicLoad(fullPath, 0, 0, BassFlags.MusicRamp | BassFlags.Decode | BassFlags.Float);
        
        if (_musicStream != 0)
        {
            // Vi använder standardvolym till att börja med.
            // Om det är tyst, kan vi justera volymen på mixernivå istället.
                
            // Lägg till i mixern. 
            // Flaggan 0x2000 (NORAMPIN) förhindrar en mjukstart (fade-in).
            bool added = BassMix.MixerAddChannel(_mixer, _musicStream, BassFlags.Default | (BassFlags)0x2000);
            
            if (added)
            {
                // Trigga igång uppspelningen i mixern
                Bass.ChannelSetPosition(_musicStream, 0);
                Console.WriteLine($"Nu spelas: {fullPath}");
            }
            else
            {
                Console.WriteLine($"Mixer-fel: {Bass.LastError}");
            }
        }
        else
        {
            Console.WriteLine($"Kunde inte ladda MOD-fil ({Bass.LastError})");
        }
    }

        public void PlaySample(string filePath, bool loop = false)
        {
            string fullPath = ResourceLoader.GetPath(filePath);
            if (!File.Exists(fullPath)) return;

            // Stoppa eventuell gammal instans av samma sample först
            StopSample(fullPath);
        
            // Lägg till Loop-flaggan om det önskas
            var flags = BassFlags.Default;
            if (loop) flags |= BassFlags.Loop;
        
            // Vi skapar en vanlig stream som INTE går via mixern. 
            // Utan BassFlags.Decode spelas den direkt på ljudkortet.
            // ÄNDRAT HÄR: Använder variabeln 'flags' istället för BassFlags.Default
            int effectStream = Bass.CreateStream(fullPath, 0, 0, flags);
        
            if (effectStream != 0)
            {
                // Spara handle så vi kan stoppa den senare
                _activeSamples[fullPath] = effectStream;
            
                // Sätt volymen lite högre för effekter om det behövs
                Bass.ChannelSetAttribute(effectStream, ChannelAttribute.Volume, 1.0);
            
                // Sätt callback för att ta bort från listan när den spelat klart (SyncEnd)
                // OBS: Om den loopar körs aldrig SyncEnd, så vi slipper städa bort den för tidigt.
                if (!loop)
                {
                    Bass.ChannelSetSync(effectStream, SyncFlags.End, 0, (handle, channel, data, user) => 
                    {
                        if (!_isDisposed)
                        {
                            lock(_activeSamples) 
                            {
                                if (_activeSamples.ContainsKey(fullPath) && _activeSamples[filePath] == effectStream)
                                {
                                    _activeSamples.Remove(fullPath);
                                }
                            }
                        }
                    });
                }
            
                // Spela direkt! Detta går förbi mixern och har minimal latency.
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
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        StopAllSamples(); // Stoppa alla ljudeffekter
        StopMod();        // Stoppa modulen
        Bass.StreamFree(_mixer);
        Bass.Free();
        _isDisposed = true;
    }
}