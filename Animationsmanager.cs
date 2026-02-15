using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AmosLikeBasic;

// ============================================================
//  AnimFrame -- ett enda steg i en animationssekvens
// ============================================================
public sealed class AnimFrame
{
    /// <summary>Vilket frame-index i sprite-sheetet som ska visas.</summary>
    public int SpriteFrameIndex { get; set; }

    /// <summary>Hur många VBL-ticks bilden ska hållas kvar (1 tick ≈ 1/60 sek).</summary>
    public int DelayTicks { get; set; }
}

// ============================================================
//  AnimationState -- en namngiven sekvens (t.ex. "idle", "run")
// ============================================================
public sealed class AnimationState
{
    /// <summary>Namn på state:t, t.ex. "idle", "run", "jump".</summary>
    public string Name { get; set; } = "";

    /// <summary>Ramarna som ingår i animationen.</summary>
    public List<AnimFrame> Frames { get; set; } = new();

    /// <summary>Om true loopar animationen, annars spelas den en gång.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Om satt: när ONCE-animationen är klar byts automatiskt till detta state.
    /// Lämna null för att frysa på sista bilden.
    /// </summary>
    public string? OnCompleteGoTo { get; set; }
}

// ============================================================
//  SpriteAnimation -- animationsdata för en specifik sprite
// ============================================================
public sealed class SpriteAnimation
{
    public int SpriteId { get; set; }

    /// <summary>Alla definierade states för denna sprite.</summary>
    public Dictionary<string, AnimationState> States { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AnimationState? CurrentState { get; set; }
    public int CurrentFrameIndex { get; set; }
    public int TicksRemaining { get; set; }
    public bool IsActive { get; set; }
}

// ============================================================
//  AnimGroup -- synkroniserar flera sprites under ett namn
// ============================================================
public sealed class AnimGroup
{
    public string Name { get; set; } = "";
    public List<int> SpriteIds { get; set; } = new();
}

// ============================================================
//  AnimationManager -- huvudklass som hanterar alla animationer
// ============================================================
public sealed class AnimationManager
{
    // -------------------------------------------------------
    //  Intern state
    // -------------------------------------------------------
    private readonly Dictionary<int, SpriteAnimation> _animations = new();
    private readonly Dictionary<string, AnimGroup> _groups =
        new(StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------
    //  ANIM DEF  --  definiera ett state för en sprite
    //  BASIC: ANIM DEF 1, "idle", (0,8)(1,8)(2,8), LOOP
    //  BASIC: ANIM DEF 1, "jump", (8,5)(9,5)(10,12), ONCE, "idle"
    // -------------------------------------------------------
    public void Define(
        int spriteId,
        string stateName,
        List<AnimFrame> frames,
        bool loop,
        string? onComplete = null)
    {
        if (frames == null || frames.Count == 0)
            return;

        if (!_animations.TryGetValue(spriteId, out var anim))
        {
            anim = new SpriteAnimation { SpriteId = spriteId };
            _animations[spriteId] = anim;
        }

        anim.States[stateName] = new AnimationState
        {
            Name          = stateName,
            Frames        = frames,
            Loop          = loop,
            OnCompleteGoTo = onComplete
        };
    }

    // -------------------------------------------------------
    //  ANIM SET  --  byt aktivt state för en sprite
    //  BASIC: ANIM SET 1, "run"
    // -------------------------------------------------------
    public void SetState(int spriteId, string stateName, bool forceRestart = false)
    {
        if (!_animations.TryGetValue(spriteId, out var anim))
            return;

        if (!anim.States.TryGetValue(stateName, out var state))
            return;

        // Undvik att restarta samma state om det redan är aktivt
        if (!forceRestart && anim.CurrentState?.Name == stateName && anim.IsActive)
            return;

        anim.CurrentState      = state;
        anim.CurrentFrameIndex = 0;
        anim.TicksRemaining    = state.Frames[0].DelayTicks;
        anim.IsActive          = true;
    }

    // -------------------------------------------------------
    //  ANIM STOP  --  stoppa animationen för en sprite
    //  BASIC: ANIM STOP 1
    // -------------------------------------------------------
    public void Stop(int spriteId)
    {
        if (_animations.TryGetValue(spriteId, out var anim))
            anim.IsActive = false;
    }

    // -------------------------------------------------------
    //  ANIM GROUP  --  koppla ihop sprites till en grupp
    //  BASIC: ANIM GROUP "player", 1, 2
    // -------------------------------------------------------
    public void DefineGroup(string groupName, params int[] spriteIds)
    {
        _groups[groupName] = new AnimGroup
        {
            Name      = groupName,
            SpriteIds = spriteIds.ToList()
        };
    }

    // -------------------------------------------------------
    //  ANIM GROUP SET  --  sätt state för hela gruppen synkat
    //  BASIC: ANIM GROUP SET "player", "run"
    // -------------------------------------------------------
    public void SetGroupState(string groupName, string stateName)
    {
        if (!_groups.TryGetValue(groupName, out var group))
            return;

        foreach (int id in group.SpriteIds)
            SetState(id, stateName, forceRestart: true);
    }

    // -------------------------------------------------------
    //  Tick()  --  anropas EN gång per WAIT VBL, INNAN EndFrame
    //  Uppdaterar alla aktiva animationer och anropar
    //  graphics.SetSpriteFrame() när nästa bild ska visas.
    // -------------------------------------------------------
    public void Tick(AmosGraphics graphics)
    {
        foreach (var anim in _animations.Values)
        {
            if (!anim.IsActive || anim.CurrentState == null)
                continue;

            anim.TicksRemaining--;

            if (anim.TicksRemaining > 0)
                continue;

            // Stega till nästa frame i sekvensen
            anim.CurrentFrameIndex++;

            if (anim.CurrentFrameIndex >= anim.CurrentState.Frames.Count)
            {
                if (anim.CurrentState.Loop)
                {
                    // Loopa om från början
                    anim.CurrentFrameIndex = 0;
                }
                else if (anim.CurrentState.OnCompleteGoTo != null)
                {
                    // ONCE med automatiskt state-byte när klar
                    SetState(anim.SpriteId, anim.CurrentState.OnCompleteGoTo, forceRestart: true);
                    continue;
                }
                else
                {
                    // ONCE utan fortsättning: frys på sista bilden
                    anim.CurrentFrameIndex = anim.CurrentState.Frames.Count - 1;
                    anim.IsActive          = false;
                    continue;
                }
            }

            // Uppdatera countdown och applicera rätt sprite-frame
            var frame = anim.CurrentState.Frames[anim.CurrentFrameIndex];
            anim.TicksRemaining = frame.DelayTicks;

            // Använder rätt metodnamn från AmosGraphics
            graphics.SetSpriteFrame(anim.SpriteId, frame.SpriteFrameIndex);
        }
    }

    // -------------------------------------------------------
    //  Hjälpmetod: returnerar nuvarande state-namn för en sprite
    //  Användbart för IF-logik i BASIC (via ANIM STATE$(1))
    // -------------------------------------------------------
    public string? GetCurrentState(int spriteId)
    {
        return _animations.TryGetValue(spriteId, out var anim)
            ? anim.CurrentState?.Name
            : null;
    }

    // -------------------------------------------------------
    //  ParseAnimArgs  --  statisk hjälp för att parsa
    //  (frame,delay)-par ur en BASIC-sträng
    //  Ex: "(0,8)(1,8)(2,8)" --> List<AnimFrame>
    // -------------------------------------------------------
    public static List<AnimFrame> ParseFrameSequence(string input)
    {
        var frames = new List<AnimFrame>();
        foreach (Match m in Regex.Matches(input, @"\((\d+),(\d+)\)"))
        {
            frames.Add(new AnimFrame
            {
                SpriteFrameIndex = int.Parse(m.Groups[1].Value),
                DelayTicks       = int.Parse(m.Groups[2].Value)
            });
        }
        return frames;
    }
}