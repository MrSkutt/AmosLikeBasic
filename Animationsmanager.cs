using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AmosLikeBasic;

// ============================================================
//  AnimFrame -- a single step in an animation sequence
// ============================================================
public sealed class AnimFrame
{
    /// <summary>Which frame index in the sprite sheet to show.</summary>
    public int SpriteFrameIndex { get; set; }

    /// <summary>How many VBL ticks the picture is held (1 tick ≈ 1/60 s).</summary>
    public int DelayTicks { get; set; }
}

// ============================================================
//  AnimationState -- a named sequence (e.g. "idle", "run")
// ============================================================
public sealed class AnimationState
{
    /// <summary>Name of the state, e.g. "idle", "run", "jump".</summary>
    public string Name { get; set; } = "";

    /// <summary>The frames in the animation.</summary>
    public List<AnimFrame> Frames { get; set; } = new();

    /// <summary>If true the animation loops, otherwise it plays once.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// If set: when the ONCE animation is done it switches automatically to this state.
    /// Leave null to freeze on the last picture.
    /// </summary>
    public string? OnCompleteGoTo { get; set; }
}

// ============================================================
//  SpriteAnimation -- animation data for one sprite
// ============================================================
public sealed class SpriteAnimation
{
    public int SpriteId { get; set; }

    /// <summary>All defined states for this sprite.</summary>
    public Dictionary<string, AnimationState> States { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AnimationState? CurrentState { get; set; }
    public int CurrentFrameIndex { get; set; }
    public int TicksRemaining { get; set; }
    public bool IsActive { get; set; }
}

// ============================================================
//  AnimGroup -- synchronises several sprites under one name
// ============================================================
public sealed class AnimGroup
{
    public string Name { get; set; } = "";
    public List<int> SpriteIds { get; set; } = new();
}

// ============================================================
//  AnimationManager -- the main class that handles all animations
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
    //  ANIM DEF  --  define a state for a sprite
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
    //  ANIM SET  --  change the active state of a sprite
    //  BASIC: ANIM SET 1, "run"
    // -------------------------------------------------------
    public void SetState(int spriteId, string stateName, bool forceRestart = false)
    {
        if (!_animations.TryGetValue(spriteId, out var anim))
            return;

        if (!anim.States.TryGetValue(stateName, out var state))
            return;

        // Avoid restarting the same state if it is already active
        if (!forceRestart && anim.CurrentState?.Name == stateName && anim.IsActive)
            return;

        anim.CurrentState      = state;
        anim.CurrentFrameIndex = 0;
        anim.TicksRemaining    = state.Frames[0].DelayTicks;
        anim.IsActive          = true;
    }

    // -------------------------------------------------------
    //  ANIM STOP  --  stop the animation of a sprite
    //  BASIC: ANIM STOP 1
    // -------------------------------------------------------
    public void Stop(int spriteId)
    {
        if (_animations.TryGetValue(spriteId, out var anim))
            anim.IsActive = false;
    }

    // -------------------------------------------------------
    //  ANIM GROUP  --  link sprites into a group
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
    //  ANIM GROUP SET  --  set the state for the whole group in sync
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
    //  Tick()  --  called ONCE per WAIT VBL, BEFORE EndFrame
    //  Updates all active animations and calls
    //  graphics.SetSpriteFrame() when the next picture should show.
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

            // Step to the next frame in the sequence
            anim.CurrentFrameIndex++;

            if (anim.CurrentFrameIndex >= anim.CurrentState.Frames.Count)
            {
                if (anim.CurrentState.Loop)
                {
                    // Loop from the start
                    anim.CurrentFrameIndex = 0;
                }
                else if (anim.CurrentState.OnCompleteGoTo != null)
                {
                    // ONCE with an automatic state change when done
                    SetState(anim.SpriteId, anim.CurrentState.OnCompleteGoTo, forceRestart: true);
                    continue;
                }
                else
                {
                    // ONCE without a follow-up: freeze on the last picture
                    anim.CurrentFrameIndex = anim.CurrentState.Frames.Count - 1;
                    anim.IsActive          = false;
                    continue;
                }
            }

            // Update the countdown and apply the right sprite frame
            var frame = anim.CurrentState.Frames[anim.CurrentFrameIndex];
            anim.TicksRemaining = frame.DelayTicks;

            // Uses the right method name from AmosGraphics
            graphics.SetSpriteFrame(anim.SpriteId, frame.SpriteFrameIndex);
        }
    }

    // -------------------------------------------------------
    //  Helper: returns the current state name of a sprite
    //  Useful for IF logic in BASIC (via ANIM STATE$(1))
    // -------------------------------------------------------
    public string? GetCurrentState(int spriteId)
    {
        return _animations.TryGetValue(spriteId, out var anim)
            ? anim.CurrentState?.Name
            : null;
    }

    // -------------------------------------------------------
    //  ParseAnimArgs  --  static helper for parsing
    //  (frame,delay) pairs from a BASIC string
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