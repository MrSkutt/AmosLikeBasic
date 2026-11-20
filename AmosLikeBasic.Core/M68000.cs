using System;

namespace AmosLikeBasic;

// What the 68000 reads and writes: memory and the hardware behind it. Addresses are 24 bits.
public interface IM68000Bus
{
    byte Read8(uint address);
    ushort Read16(uint address);
    void Write8(uint address, byte value);
    void Write16(uint address, ushort value);
}

// The Motorola 68000, as in the Atari ST: eight data and eight address registers, the status register
// with its flags (X N Z V C) and supervisor bit, and the instruction set. Enough to run Atari music
// routines (SNDH files); cycle counts are an estimate.
public sealed class M68000
{
    public readonly uint[] D = new uint[8];
    public readonly uint[] A = new uint[8];   // A[7] is the stack pointer in use
    public uint PC;
    public long Cycles;
    public bool Stopped;
    private ushort _sr = 0x2700;
    private uint _otherStack;                 // the stack pointer of the mode not in use
    private readonly IM68000Bus _bus;

    // A trap the host handles itself (GEMDOS, XBIOS): returns true if it did
    public Func<int, bool>? Trap;

    public M68000(IM68000Bus bus) => _bus = bus;

    private const int C = 1, V = 2, Z = 4, N = 8, X = 16;

    public ushort SR
    {
        get => _sr;
        set
        {
            bool wasSuper = (_sr & 0x2000) != 0, isSuper = (value & 0x2000) != 0;
            if (wasSuper != isSuper) (A[7], _otherStack) = (_otherStack, A[7]);
            _sr = (ushort)(value & 0xA71F);
        }
    }

    public bool Supervisor => (_sr & 0x2000) != 0;

    // Both stack pointers, whichever is in use
    public uint UserStack => Supervisor ? _otherStack : A[7];
    public uint SupervisorStack => Supervisor ? A[7] : _otherStack;

    // Sets the status register and both stack pointers at once, as at the start of a test or a program
    public void SetState(ushort sr, uint userStack, uint supervisorStack)
    {
        _sr = (ushort)(sr & 0xA71F);
        A[7] = Supervisor ? supervisorStack : userStack;
        _otherStack = Supervisor ? userStack : supervisorStack;
    }
    public int InterruptMask => (_sr >> 8) & 7;

    private bool Flag(int f) => (_sr & f) != 0;
    private void SetFlag(int f, bool on) => _sr = (ushort)(on ? _sr | f : _sr & ~f);

    // ── Memory ──

    private byte R8(uint a) => _bus.Read8(a & 0xFFFFFF);
    private ushort R16(uint a) => _bus.Read16(a & 0xFFFFFE);
    private uint R32(uint a) => (uint)R16(a) << 16 | R16(a + 2);
    private void W8(uint a, uint v) => _bus.Write8(a & 0xFFFFFF, (byte)v);
    private void W16(uint a, uint v) => _bus.Write16(a & 0xFFFFFE, (ushort)v);
    private void W32(uint a, uint v) { W16(a, v >> 16); W16(a + 2, v); }

    private ushort Fetch16() { ushort w = R16(PC); PC += 2; return w; }
    private uint Fetch32() { uint l = R32(PC); PC += 4; return l; }

    public void Push32(uint v) { A[7] -= 4; W32(A[7], v); }
    private void Push16(uint v) { A[7] -= 2; W16(A[7], v); }
    private uint Pop32() { uint v = R32(A[7]); A[7] += 4; return v; }
    private ushort Pop16() { ushort v = R16(A[7]); A[7] += 2; return v; }

    // ── Sizes ──

    private static uint Mask(int size) => size == 1 ? 0xFFu : size == 2 ? 0xFFFFu : 0xFFFFFFFFu;
    private static uint MsbOf(int size) => size == 1 ? 0x80u : size == 2 ? 0x8000u : 0x80000000u;
    private static int SizeOf(int bits) => bits == 0 ? 1 : bits == 1 ? 2 : 4;   // the usual 00/01/10 size field
    private static uint SignExtend(uint v, int size) => size == 1 ? (uint)(sbyte)v : size == 2 ? (uint)(short)v : v;

    // ── Effective addresses ──

    // Where an operand is: a data or address register, memory, or an immediate value
    private struct Ea
    {
        public int Kind;      // 0 data register, 1 address register, 2 memory, 3 immediate
        public int Reg;
        public uint Address;
        public uint Value;    // the immediate value
    }

    private Ea Resolve(int mode, int reg, int size)
    {
        switch (mode)
        {
            case 0: return new Ea { Kind = 0, Reg = reg };
            case 1: return new Ea { Kind = 1, Reg = reg };
            case 2: return new Ea { Kind = 2, Address = A[reg] };
            case 3:
            {
                uint a = A[reg];
                A[reg] += (uint)(size == 1 && reg == 7 ? 2 : size);   // the stack pointer stays even
                return new Ea { Kind = 2, Address = a };
            }
            case 4:
                A[reg] -= (uint)(size == 1 && reg == 7 ? 2 : size);
                Cycles += 2;
                return new Ea { Kind = 2, Address = A[reg] };
            case 5: return new Ea { Kind = 2, Address = A[reg] + (uint)(short)Fetch16() };
            case 6: return new Ea { Kind = 2, Address = Indexed(A[reg]) };
            default:
                switch (reg)
                {
                    case 0: return new Ea { Kind = 2, Address = (uint)(short)Fetch16() };
                    case 1: return new Ea { Kind = 2, Address = Fetch32() };
                    case 2: { uint pc = PC; return new Ea { Kind = 2, Address = pc + (uint)(short)Fetch16() }; }
                    case 3: { uint pc = PC; return new Ea { Kind = 2, Address = Indexed(pc) }; }
                    case 4:
                        if (size == 4) return new Ea { Kind = 3, Value = Fetch32() };
                        uint w = Fetch16();
                        return new Ea { Kind = 3, Value = size == 1 ? w & 0xFF : w };
                }
                break;
        }
        throw new IllegalInstruction();
    }

    // d8(base, Xn.W/L)
    private uint Indexed(uint b)
    {
        ushort ext = Fetch16();
        int r = (ext >> 12) & 7;
        uint index = (ext & 0x8000) != 0 ? A[r] : D[r];
        if ((ext & 0x0800) == 0) index = (uint)(short)index;
        Cycles += 2;
        return b + index + (uint)(sbyte)(ext & 0xFF);
    }

    private uint Read(in Ea ea, int size)
    {
        switch (ea.Kind)
        {
            case 0: return D[ea.Reg] & Mask(size);
            case 1: return A[ea.Reg] & Mask(size);
            case 3: return ea.Value;
        }
        Cycles += size == 4 ? 8 : 4;
        return size == 1 ? R8(ea.Address) : size == 2 ? R16(ea.Address) : R32(ea.Address);
    }

    private void Write(in Ea ea, int size, uint value)
    {
        switch (ea.Kind)
        {
            case 0: D[ea.Reg] = (D[ea.Reg] & ~Mask(size)) | (value & Mask(size)); return;
            case 1: A[ea.Reg] = value; return;
            case 3: throw new IllegalInstruction();
        }
        Cycles += size == 4 ? 8 : 4;
        if (size == 1) W8(ea.Address, value);
        else if (size == 2) W16(ea.Address, value);
        else W32(ea.Address, value);
    }

    private sealed class IllegalInstruction : Exception { }

    // ── Flags ──

    private void SetNZ(uint result, int size)
    {
        result &= Mask(size);
        _sr = (ushort)((_sr & ~(N | Z | V | C)) | ((result & MsbOf(size)) != 0 ? N : 0) | (result == 0 ? Z : 0));
    }

    private uint Add(uint s, uint d, int size, bool withX = false)
    {
        uint x = withX && Flag(X) ? 1u : 0;
        ulong r = (ulong)(s & Mask(size)) + (d & Mask(size)) + x;
        uint res = (uint)r & Mask(size);
        uint msb = MsbOf(size);
        bool carry = (r >> (size * 8)) != 0;
        bool overflow = ((~(s ^ d) & (res ^ d)) & msb) != 0;
        int f = (carry ? C | X : 0) | (overflow ? V : 0) | ((res & msb) != 0 ? N : 0);
        if (withX) f |= res == 0 ? _sr & Z : 0;   // ADDX: Z only cleared
        else f |= res == 0 ? Z : 0;
        _sr = (ushort)((_sr & ~(X | N | Z | V | C)) | f);
        return res;
    }

    private uint Sub(uint s, uint d, int size, bool withX = false, bool setX = true)
    {
        uint x = withX && Flag(X) ? 1u : 0;
        uint ms = s & Mask(size), md = d & Mask(size);
        uint res = (md - ms - x) & Mask(size);
        uint msb = MsbOf(size);
        bool borrow = (ulong)ms + x > md;
        bool overflow = (((s ^ d) & (res ^ d)) & msb) != 0;
        int f = (borrow ? C : 0) | (overflow ? V : 0) | ((res & msb) != 0 ? N : 0);
        if (withX) f |= res == 0 ? _sr & Z : 0;
        else f |= res == 0 ? Z : 0;
        int keep = setX ? ~(X | N | Z | V | C) : ~(N | Z | V | C);
        if (setX && borrow) f |= X;
        _sr = (ushort)((_sr & keep) | f);
        return res;
    }

    private bool Condition(int cc) => cc switch
    {
        0 => true,
        1 => false,
        2 => !Flag(C) && !Flag(Z),
        3 => Flag(C) || Flag(Z),
        4 => !Flag(C),
        5 => Flag(C),
        6 => !Flag(Z),
        7 => Flag(Z),
        8 => !Flag(V),
        9 => Flag(V),
        10 => !Flag(N),
        11 => Flag(N),
        12 => Flag(N) == Flag(V),
        13 => Flag(N) != Flag(V),
        14 => !Flag(Z) && Flag(N) == Flag(V),
        _ => Flag(Z) || Flag(N) != Flag(V)
    };

    // ── Exceptions and interrupts ──

    public void Reset(uint ssp, uint pc)
    {
        SR = 0x2700;
        A[7] = ssp;
        PC = pc;
        Stopped = false;
    }

    private void Exception(int vector, ushort? srBefore = null)
    {
        ushort old = srBefore ?? _sr;
        SR = (ushort)((_sr | 0x2000) & ~0x8000);
        Push32(PC);
        Push16(old);
        PC = R32((uint)vector * 4);
        Cycles += 34;
    }

    // An interrupt of a level (1 – 7) is taken if it is above the mask; returns whether it was
    public bool Interrupt(int level, int vector)
    {
        if (level <= InterruptMask && level < 7) return false;
        ushort old = _sr;
        Exception(vector, old);
        _sr = (ushort)((_sr & ~0x0700) | (level << 8));
        Stopped = false;
        return true;
    }

    // ── Running ──

    // One instruction
    public void Step()
    {
        if (Stopped) { Cycles += 4; return; }
        uint start = PC;
        try
        {
            ushort op = Fetch16();
            Cycles += 4;
            Execute(op);
        }
        catch (IllegalInstruction)
        {
            PC = start;
            Exception(4);
        }
    }

    private void Execute(ushort op)
    {
        switch (op >> 12)
        {
            case 0x0: Line0(op); break;
            case 0x1: Move(op, 1); break;
            case 0x2: Move(op, 4); break;
            case 0x3: Move(op, 2); break;
            case 0x4: Line4(op); break;
            case 0x5: Line5(op); break;
            case 0x6: Branch(op); break;
            case 0x7:
                if ((op & 0x100) != 0) throw new IllegalInstruction();
                D[(op >> 9) & 7] = (uint)(sbyte)op;
                SetNZ((uint)(sbyte)op, 4);
                break;
            case 0x8: Line8(op); break;
            case 0x9: AddSub(op, add: false); break;
            case 0xA: PC -= 2; Exception(10); break;
            case 0xB: LineB(op); break;
            case 0xC: LineC(op); break;
            case 0xD: AddSub(op, add: true); break;
            case 0xE: Shift(op); break;
            default: PC -= 2; Exception(11); break;
        }
    }

    // ── MOVE ──

    private void Move(ushort op, int size)
    {
        var src = Resolve((op >> 3) & 7, op & 7, size);
        uint v = Read(src, size);
        int dmode = (op >> 6) & 7, dreg = (op >> 9) & 7;
        if (dmode == 1)
        {
            A[dreg] = SignExtend(v, size);   // MOVEA: no flags
            return;
        }
        var dst = Resolve(dmode, dreg, size);
        Write(dst, size, v);
        SetNZ(v, size);
    }

    // ── Line 0: bits, immediates, MOVEP ──

    private void Line0(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7;
        if ((op & 0x0100) != 0)
        {
            if (mode == 1) { Movep(op); return; }
            Bit(op, D[(op >> 9) & 7]);
            return;
        }
        int kind = (op >> 9) & 7;
        if (kind == 4) { Bit(op, Fetch16()); return; }
        int sizeBits = (op >> 6) & 3;
        if (sizeBits == 3) throw new IllegalInstruction();
        int size = SizeOf(sizeBits);

        // ORI / ANDI / EORI to CCR and SR
        if (mode == 7 && reg == 4 && kind is 0 or 1 or 5)
        {
            uint imm = Fetch16();
            ushort value = size == 1 ? (ushort)((_sr & 0xFF00) | imm & 0xFF) : (ushort)imm;
            if (size == 2 && !Supervisor) { PC -= 4; Exception(8); return; }
            ushort current = _sr;
            ushort result = kind switch { 0 => (ushort)(current | value), 1 => (ushort)(current & value), _ => (ushort)(current ^ value) };
            if (size == 1) result = (ushort)((current & 0xFF00) | (result & 0x1F));
            SR = result;
            return;
        }

        uint immediate = size == 4 ? Fetch32() : size == 2 ? Fetch16() : (uint)(Fetch16() & 0xFF);
        var ea = Resolve(mode, reg, size);
        uint d = Read(ea, size);
        switch (kind)
        {
            case 0: d |= immediate; Write(ea, size, d); SetNZ(d, size); break;
            case 1: d &= immediate; Write(ea, size, d); SetNZ(d, size); break;
            case 2: Write(ea, size, Sub(immediate, d, size)); break;
            case 3: Write(ea, size, Add(immediate, d, size)); break;
            case 5: d ^= immediate; Write(ea, size, d); SetNZ(d, size); break;
            case 6: Sub(immediate, d, size, setX: false); break;
            default: throw new IllegalInstruction();
        }
    }

    // BTST / BCHG / BCLR / BSET: a register's bit (0 – 31) or a byte in memory (0 – 7)
    private void Bit(ushort op, uint bit)
    {
        int mode = (op >> 3) & 7, reg = op & 7, kind = (op >> 6) & 3;
        if (mode == 0)
        {
            uint m = 1u << (int)(bit & 31);
            SetFlag(Z, (D[reg] & m) == 0);
            switch (kind)
            {
                case 1: D[reg] ^= m; break;
                case 2: D[reg] &= ~m; break;
                case 3: D[reg] |= m; break;
            }
            return;
        }
        var ea = Resolve(mode, reg, 1);
        uint v = Read(ea, 1);
        uint mb = 1u << (int)(bit & 7);
        SetFlag(Z, (v & mb) == 0);
        if (kind == 0) return;
        v = kind == 1 ? v ^ mb : kind == 2 ? v & ~mb : v | mb;
        Write(ea, 1, v);
    }

    // MOVEP: a register to or from every other byte (how YM registers are often written)
    private void Movep(ushort op)
    {
        int dreg = (op >> 9) & 7, areg = op & 7, opmode = (op >> 6) & 7;
        uint address = A[areg] + (uint)(short)Fetch16();
        bool longSize = (opmode & 1) != 0, toMemory = (opmode & 2) != 0;
        int bytes = longSize ? 4 : 2;
        if (toMemory)
        {
            for (int i = 0; i < bytes; i++) W8(address + (uint)(i * 2), D[dreg] >> (8 * (bytes - 1 - i)));
        }
        else
        {
            uint v = 0;
            for (int i = 0; i < bytes; i++) v = v << 8 | R8(address + (uint)(i * 2));
            D[dreg] = longSize ? v : (D[dreg] & 0xFFFF0000) | v;
        }
        Cycles += bytes * 4;
    }

    // ── Line 4: miscellaneous ──

    private void Line4(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7;
        switch (op)
        {
            case 0x4E70: if (!Supervisor) { PC -= 2; Exception(8); } return;          // RESET
            case 0x4E71: return;                                                     // NOP
            case 0x4E72:                                                             // STOP
            {
                ushort v = Fetch16();
                if (!Supervisor) { PC -= 4; Exception(8); return; }
                SR = v;
                Stopped = true;
                return;
            }
            case 0x4E73:                                                             // RTE
            {
                if (!Supervisor) { PC -= 2; Exception(8); return; }
                ushort sr = Pop16();
                PC = Pop32();
                SR = sr;
                return;
            }
            case 0x4E75: PC = Pop32(); return;                                       // RTS
            case 0x4E76: if (Flag(V)) Exception(7); return;                          // TRAPV
            case 0x4E77:                                                             // RTR
                _sr = (ushort)((_sr & 0xFF00) | (Pop16() & 0x1F));
                PC = Pop32();
                return;
            case 0x4AFC: PC -= 2; Exception(4); return;                              // ILLEGAL
        }
        if ((op & 0xFFF0) == 0x4E40)                                                 // TRAP #n
        {
            int n = op & 15;
            if (Trap?.Invoke(n) == true) return;
            Exception(32 + n);
            return;
        }
        if ((op & 0xFFF8) == 0x4E50)                                                 // LINK
        {
            // LINK A7 stores the stack pointer as it is after the push
            Push32(reg == 7 ? A[7] - 4 : A[reg]);
            A[reg] = A[7];
            A[7] += (uint)(short)Fetch16();
            return;
        }
        if ((op & 0xFFF8) == 0x4E58)                                                 // UNLK
        {
            A[7] = A[reg];
            A[reg] = Pop32();
            return;
        }
        if ((op & 0xFFF0) == 0x4E60)                                                 // MOVE An <-> USP
        {
            if (!Supervisor) { PC -= 2; Exception(8); return; }
            if ((op & 8) == 0) _otherStack = A[reg];
            else A[reg] = _otherStack;
            return;
        }
        if ((op & 0xFFC0) == 0x4E80) { uint a = Resolve(mode, reg, 4).Address; Push32(PC); PC = a; Cycles += 8; return; }   // JSR
        if ((op & 0xFFC0) == 0x4EC0) { PC = Resolve(mode, reg, 4).Address; Cycles += 4; return; }                            // JMP
        if ((op & 0xF1C0) == 0x41C0) { A[(op >> 9) & 7] = Resolve(mode, reg, 4).Address; return; }                          // LEA
        if ((op & 0xF1C0) == 0x4180)                                                                                          // CHK.W
        {
            var ea = Resolve(mode, reg, 2);
            short bound = (short)Read(ea, 2), value = (short)D[(op >> 9) & 7];
            // Z, V and C as the real 68000 leaves them (not documented)
            SetFlag(Z, value == 0);
            SetFlag(V, false);
            SetFlag(C, false);
            if (value < 0) { SetFlag(N, true); Exception(6); }
            else if (value > bound) { SetFlag(N, false); Exception(6); }
            return;
        }
        if ((op & 0xFFC0) == 0x40C0) { var ea = Resolve(mode, reg, 2); Write(ea, 2, _sr); return; }                          // MOVE from SR
        if ((op & 0xFFC0) == 0x44C0)                                                                                          // MOVE to CCR
        {
            var ea = Resolve(mode, reg, 2);
            _sr = (ushort)((_sr & 0xFF00) | (Read(ea, 2) & 0x1F));
            return;
        }
        if ((op & 0xFFC0) == 0x46C0)                                                                                          // MOVE to SR
        {
            if (!Supervisor) { PC -= 2; Exception(8); return; }
            var ea = Resolve(mode, reg, 2);
            SR = (ushort)Read(ea, 2);
            return;
        }
        if ((op & 0xFFC0) == 0x4800) { Nbcd(mode, reg); return; }                                                            // NBCD
        if ((op & 0xFFF8) == 0x4840) { uint v = D[reg]; v = v << 16 | v >> 16; D[reg] = v; SetNZ(v, 4); return; }          // SWAP
        if ((op & 0xFFC0) == 0x4840) { uint a = Resolve(mode, reg, 4).Address; Push32(a); return; }                           // PEA
        if ((op & 0xFFF8) == 0x4880) { uint v = (uint)(short)(sbyte)D[reg]; D[reg] = (D[reg] & 0xFFFF0000) | (v & 0xFFFF); SetNZ(v, 2); return; }   // EXT.W
        if ((op & 0xFFF8) == 0x48C0) { uint v = (uint)(short)D[reg]; D[reg] = v; SetNZ(v, 4); return; }                      // EXT.L
        if ((op & 0xFB80) == 0x4880) { Movem(op); return; }                                                                  // MOVEM
        if ((op & 0xFFC0) == 0x4AC0)                                                                                          // TAS
        {
            var ea = Resolve(mode, reg, 1);
            uint v = Read(ea, 1);
            SetNZ(v, 1);
            Write(ea, 1, v | 0x80);
            return;
        }
        int sizeBits = (op >> 6) & 3;
        if (sizeBits == 3) throw new IllegalInstruction();
        int size = SizeOf(sizeBits);
        switch (op & 0xFF00)
        {
            case 0x4000:                                                             // NEGX
            {
                var ea = Resolve(mode, reg, size);
                Write(ea, size, Sub(Read(ea, size), 0, size, withX: true));
                return;
            }
            case 0x4200:                                                             // CLR
            {
                var ea = Resolve(mode, reg, size);
                if (ea.Kind == 2) Read(ea, size);   // the 68000 reads before it clears
                Write(ea, size, 0);
                SetNZ(0, size);
                return;
            }
            case 0x4400:                                                             // NEG
            {
                var ea = Resolve(mode, reg, size);
                Write(ea, size, Sub(Read(ea, size), 0, size));
                return;
            }
            case 0x4600:                                                             // NOT
            {
                var ea = Resolve(mode, reg, size);
                uint v = ~Read(ea, size) & Mask(size);
                Write(ea, size, v);
                SetNZ(v, size);
                return;
            }
            case 0x4A00:                                                             // TST
            {
                var ea = Resolve(mode, reg, size);
                SetNZ(Read(ea, size), size);
                return;
            }
        }
        throw new IllegalInstruction();
    }

    // MOVEM: the registers of a mask to or from memory; to -(An) the mask is reversed (A7 first)
    private void Movem(ushort op)
    {
        bool toRegisters = (op & 0x0400) != 0;
        int size = (op & 0x0040) != 0 ? 4 : 2;
        int mode = (op >> 3) & 7, reg = op & 7;
        ushort mask = Fetch16();
        if (!toRegisters && mode == 4)
        {
            uint a = A[reg];
            for (int i = 0; i < 16; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                int r = 15 - i;   // bit 0 is A7
                a -= (uint)size;
                uint v = r >= 8 ? A[r - 8] : D[r];
                if (size == 2) W16(a, v); else W32(a, v);
                Cycles += size * 2;
            }
            A[reg] = a;
            return;
        }
        uint address = mode == 3 ? A[reg] : Resolve(mode, reg, size).Address;
        for (int r = 0; r < 16; r++)
        {
            if ((mask & (1 << r)) == 0) continue;
            if (toRegisters)
            {
                uint v = size == 2 ? (uint)(short)R16(address) : R32(address);
                if (r >= 8) A[r - 8] = v; else D[r] = v;
            }
            else
            {
                uint v = r >= 8 ? A[r - 8] : D[r];
                if (size == 2) W16(address, v); else W32(address, v);
            }
            address += (uint)size;
            Cycles += size * 2;
        }
        if (mode == 3) A[reg] = address;
    }

    // ── Line 5: ADDQ, SUBQ, Scc, DBcc ──

    private void Line5(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7;
        int sizeBits = (op >> 6) & 3;
        if (sizeBits == 3)
        {
            int cc = (op >> 8) & 15;
            if (mode == 1)
            {
                // DBcc: unless the condition holds, count down and branch until -1
                uint pc = PC;
                short disp = (short)Fetch16();
                if (Condition(cc)) return;
                ushort count = (ushort)(D[reg] - 1);
                D[reg] = (D[reg] & 0xFFFF0000) | count;
                if (count != 0xFFFF) { PC = pc + (uint)disp; Cycles += 6; }
                return;
            }
            var ea = Resolve(mode, reg, 1);
            if (ea.Kind == 2) Read(ea, 1);
            Write(ea, 1, Condition(cc) ? 0xFFu : 0);
            return;
        }
        int size = SizeOf(sizeBits);
        uint q = (uint)((op >> 9) & 7);
        if (q == 0) q = 8;
        bool sub = (op & 0x0100) != 0;
        if (mode == 1)
        {
            // To an address register: all 32 bits, no flags
            A[reg] = sub ? A[reg] - q : A[reg] + q;
            return;
        }
        var target = Resolve(mode, reg, size);
        uint d = Read(target, size);
        Write(target, size, sub ? Sub(q, d, size) : Add(q, d, size));
    }

    // ── Line 6: Bcc, BRA, BSR ──

    private void Branch(ushort op)
    {
        uint pc = PC;
        int disp = (sbyte)(op & 0xFF);
        if (disp == 0) disp = (short)Fetch16();
        int cc = (op >> 8) & 15;
        if (cc == 1) { Push32(PC); PC = pc + (uint)disp; Cycles += 8; return; }   // BSR
        if (Condition(cc)) { PC = pc + (uint)disp; Cycles += 6; }
    }

    // ── Line 8: OR, DIVU, DIVS, SBCD ──

    private void Line8(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7, dreg = (op >> 9) & 7;
        int opmode = (op >> 6) & 7;
        if (opmode == 3 || opmode == 7) { Divide(dreg, mode, reg, signed: opmode == 7); return; }
        if ((op & 0x01F0) == 0x0100) { Bcd(op, add: false); return; }
        LogicOp(op, (a, b) => a | b);
    }

    private void Divide(int dreg, int mode, int reg, bool signed)
    {
        var ea = Resolve(mode, reg, 2);
        uint divisor = Read(ea, 2);
        Cycles += signed ? 150 : 130;
        if (divisor == 0) { _sr = (ushort)(_sr & ~(N | Z | V | C)); Exception(5); return; }
        uint dividend = D[dreg];
        if (signed)
        {
            long q = (long)(int)dividend / (short)divisor, r = (long)(int)dividend % (short)divisor;
            if (q < short.MinValue || q > short.MaxValue) { SetFlag(V, true); SetFlag(C, false); return; }
            D[dreg] = (uint)((ushort)r << 16 | (ushort)q);
            SetNZ((uint)q, 2);
        }
        else
        {
            uint q = dividend / divisor, r = dividend % divisor;
            if (q > 0xFFFF) { SetFlag(V, true); SetFlag(C, false); return; }
            D[dreg] = r << 16 | q;
            SetNZ(q, 2);
        }
    }

    // OR and AND: <ea> to Dn, or Dn to <ea>
    private void LogicOp(ushort op, Func<uint, uint, uint> f)
    {
        int mode = (op >> 3) & 7, reg = op & 7, dreg = (op >> 9) & 7;
        int size = SizeOf((op >> 6) & 3);
        var ea = Resolve(mode, reg, size);
        if ((op & 0x0100) == 0)
        {
            uint v = f(Read(ea, size), D[dreg]) & Mask(size);
            D[dreg] = (D[dreg] & ~Mask(size)) | v;
            SetNZ(v, size);
        }
        else
        {
            uint v = f(Read(ea, size), D[dreg]) & Mask(size);
            Write(ea, size, v);
            SetNZ(v, size);
        }
    }

    // ABCD / SBCD: binary-coded decimal bytes, register to register or -(Ay) to -(Ax)
    private void Bcd(ushort op, bool add)
    {
        int rx = (op >> 9) & 7, ry = op & 7;
        bool memory = (op & 8) != 0;
        uint s, d;
        Ea dst;
        if (memory)
        {
            s = Read(Resolve(4, ry, 1), 1);
            dst = Resolve(4, rx, 1);
            d = Read(dst, 1);
        }
        else
        {
            s = D[ry] & 0xFF;
            dst = new Ea { Kind = 0, Reg = rx };
            d = D[rx] & 0xFF;
        }
        // As the real 68000, with its undocumented V and N: V is set when the decimal correction
        // turned a positive byte negative
        uint x = Flag(X) ? 1u : 0;
        uint res, v;
        bool carry;
        if (add)
        {
            res = (s & 15) + (d & 15) + x;
            v = ~res;
            if (res > 9) res += 6;
            res += (s & 0xF0) + (d & 0xF0);
            carry = res > 0x99;
            if (carry) res -= 0xA0;
        }
        else
        {
            res = (d & 15) - (s & 15) - x;
            v = ~res;
            if (res > 9) res -= 6;
            res += (d & 0xF0) - (s & 0xF0);
            carry = res > 0x99;
            if (carry) res += 0xA0;
        }
        res &= 0xFF;
        Write(dst, 1, res);
        SetFlag(C, carry);
        SetFlag(X, carry);
        SetFlag(V, (v & res & 0x80) != 0);
        SetFlag(N, (res & 0x80) != 0);
        if (res != 0) SetFlag(Z, false);
    }

    private void Nbcd(int mode, int reg)
    {
        var ea = Resolve(mode, reg, 1);
        uint d = Read(ea, 1);
        uint res = (0x9Au - d - (Flag(X) ? 1u : 0)) & 0xFF;
        if (res != 0x9A)
        {
            uint v = ~res;
            if ((res & 0x0F) == 0x0A) res = (res & 0xF0) + 0x10;
            res &= 0xFF;
            Write(ea, 1, res);
            SetFlag(V, (v & res & 0x80) != 0);
            if (res != 0) SetFlag(Z, false);
            SetFlag(C, true);
            SetFlag(X, true);
        }
        else
        {
            SetFlag(V, false);
            SetFlag(C, false);
            SetFlag(X, false);
        }
        SetFlag(N, (res & 0x80) != 0);
    }

    // ── Lines 9 and D: SUB and ADD, with A and X ──

    private void AddSub(ushort op, bool add)
    {
        int mode = (op >> 3) & 7, reg = op & 7, dreg = (op >> 9) & 7;
        int opmode = (op >> 6) & 7;
        if (opmode == 3 || opmode == 7)
        {
            // ADDA / SUBA: no flags, a word is sign-extended
            int size = opmode == 3 ? 2 : 4;
            uint v = SignExtend(Read(Resolve(mode, reg, size), size), size);
            A[dreg] = add ? A[dreg] + v : A[dreg] - v;
            Cycles += 4;
            return;
        }
        int sz = SizeOf(opmode & 3);
        if ((opmode & 4) != 0 && mode is 0 or 1)
        {
            // ADDX / SUBX: Dy to Dx, or -(Ay) to -(Ax)
            if (mode == 0)
            {
                uint r = add ? Add(D[reg], D[dreg], sz, withX: true) : Sub(D[reg], D[dreg], sz, withX: true);
                D[dreg] = (D[dreg] & ~Mask(sz)) | r;
            }
            else
            {
                uint s = Read(Resolve(4, reg, sz), sz);
                var dst = Resolve(4, dreg, sz);
                uint d = Read(dst, sz);
                Write(dst, sz, add ? Add(s, d, sz, withX: true) : Sub(s, d, sz, withX: true));
            }
            return;
        }
        var ea = Resolve(mode, reg, sz);
        if ((opmode & 4) == 0)
        {
            uint r = add ? Add(Read(ea, sz), D[dreg], sz) : Sub(Read(ea, sz), D[dreg], sz);
            D[dreg] = (D[dreg] & ~Mask(sz)) | r;
        }
        else
        {
            uint d = Read(ea, sz);
            Write(ea, sz, add ? Add(D[dreg], d, sz) : Sub(D[dreg], d, sz));
        }
    }

    // ── Line B: CMP, CMPA, CMPM, EOR ──

    private void LineB(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7, dreg = (op >> 9) & 7;
        int opmode = (op >> 6) & 7;
        if (opmode == 3 || opmode == 7)
        {
            int size = opmode == 3 ? 2 : 4;
            uint v = SignExtend(Read(Resolve(mode, reg, size), size), size);
            Sub(v, A[dreg], 4, setX: false);
            return;
        }
        int sz = SizeOf(opmode & 3);
        if ((opmode & 4) == 0)
        {
            Sub(Read(Resolve(mode, reg, sz), sz), D[dreg], sz, setX: false);
            return;
        }
        if (mode == 1)
        {
            // CMPM (Ay)+,(Ax)+
            uint s = Read(Resolve(3, reg, sz), sz);
            uint d = Read(Resolve(3, dreg, sz), sz);
            Sub(s, d, sz, setX: false);
            return;
        }
        var ea = Resolve(mode, reg, sz);
        uint e = (Read(ea, sz) ^ D[dreg]) & Mask(sz);
        Write(ea, sz, e);
        SetNZ(e, sz);
    }

    // ── Line C: AND, MULU, MULS, ABCD, EXG ──

    private void LineC(ushort op)
    {
        int mode = (op >> 3) & 7, reg = op & 7, dreg = (op >> 9) & 7;
        int opmode = (op >> 6) & 7;
        if (opmode == 3 || opmode == 7)
        {
            uint s = Read(Resolve(mode, reg, 2), 2);
            uint r = opmode == 3 ? (D[dreg] & 0xFFFF) * s : (uint)((short)D[dreg] * (short)s);
            D[dreg] = r;
            SetNZ(r, 4);
            Cycles += 50;
            return;
        }
        if ((op & 0x01F0) == 0x0100) { Bcd(op, add: true); return; }
        if ((op & 0x01F8) == 0x0140) { (D[dreg], D[reg]) = (D[reg], D[dreg]); return; }   // EXG Dx,Dy
        if ((op & 0x01F8) == 0x0148) { (A[dreg], A[reg]) = (A[reg], A[dreg]); return; }   // EXG Ax,Ay
        if ((op & 0x01F8) == 0x0188) { (D[dreg], A[reg]) = (A[reg], D[dreg]); return; }   // EXG Dx,Ay
        LogicOp(op, (a, b) => a & b);
    }

    // ── Line E: shifts and rotates ──

    private void Shift(ushort op)
    {
        int sizeBits = (op >> 6) & 3;
        bool left = (op & 0x0100) != 0;
        if (sizeBits == 3)
        {
            // In memory, a word by one place
            var ea = Resolve((op >> 3) & 7, op & 7, 2);
            uint v = Read(ea, 2);
            Write(ea, 2, ShiftValue((op >> 9) & 3, left, v, 1, 2));
            return;
        }
        int size = SizeOf(sizeBits);
        int reg = op & 7;
        int count = (op >> 9) & 7;
        if ((op & 0x0020) != 0) count = (int)(D[count] & 63);
        else if (count == 0) count = 8;
        Cycles += 2 * count;
        uint r = ShiftValue((op >> 3) & 3, left, D[reg] & Mask(size), count, size);
        D[reg] = (D[reg] & ~Mask(size)) | r;
    }

    // kind: 0 arithmetic, 1 logical, 2 rotate with X, 3 rotate
    private uint ShiftValue(int kind, bool left, uint v, int count, int size)
    {
        uint mask = Mask(size), msb = MsbOf(size);
        int bits = size * 8;
        bool carry = false, overflow = false;
        bool x = Flag(X);
        v &= mask;
        for (int i = 0; i < count; i++)
        {
            if (left)
            {
                carry = (v & msb) != 0;
                uint inBit = kind switch { 2 => x ? 1u : 0, 3 => carry ? 1u : 0, _ => 0u };
                uint next = ((v << 1) | inBit) & mask;
                if (kind == 0 && ((next ^ v) & msb) != 0) overflow = true;
                v = next;
            }
            else
            {
                carry = (v & 1) != 0;
                uint inBit = kind switch { 0 => v & msb, 2 => x ? msb : 0, 3 => carry ? msb : 0, _ => 0u };
                v = (v >> 1) | inBit;
            }
            if (kind == 2) x = carry;
        }
        int f = _sr & ~(N | Z | V | C);
        if (count > 0)
        {
            if (kind != 3) f = carry ? f | X : f & ~X;   // ROL/ROR leave X alone
            if (carry) f |= C;
        }
        else if (kind == 2 && Flag(X)) f |= C;           // ROXL/ROXR by 0: C is X
        if (overflow) f |= V;
        if ((v & msb) != 0) f |= N;
        if (v == 0) f |= Z;
        _sr = (ushort)f;
        _ = bits;
        return v;
    }
}
