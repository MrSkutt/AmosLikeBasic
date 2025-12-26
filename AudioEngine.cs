using System;
using System.IO;
using ManagedBass;
using ManagedBass.Mix;

public sealed class AudioEngine : IDisposable
{
    private readonly int _mixer;
    private int _musicStream; 
    private bool _isDisposed;

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
        if (!File.Exists(filePath))
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
        _musicStream = Bass.MusicLoad(filePath, 0, 0, BassFlags.MusicRamp | BassFlags.Decode | BassFlags.Float);
        
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
                Console.WriteLine($"Nu spelas: {filePath}");
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

    public void PlaySample(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // Vi skapar en vanlig stream som INTE går via mixern. 
        // Utan BassFlags.Decode spelas den direkt på ljudkortet.
        int effectStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
        
        if (effectStream != 0)
        {
            // Sätt volymen lite högre för effekter om det behövs
            Bass.ChannelSetAttribute(effectStream, ChannelAttribute.Volume, 1.0);
                
            // Spela direkt! Detta går förbi mixern och har minimal latency.
            Bass.ChannelPlay(effectStream);
                
            // Vi flaggar inte för AutoFree här eftersom det är en direkt-stream, 
            // men BASS städar oftast upp ändå. För korta SAM-klipp är detta säkrast.
        }
        else
        {
            Console.WriteLine($"BASS Error {Bass.LastError} vid laddning av: {filePath}");
        }
    }

    // Fix för felet i AmosRunner: Lägg till StopMod som anropas därifrån
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
        StopMod();
        Bass.StreamFree(_mixer);
        Bass.Free();
        _isDisposed = true;
    }
}