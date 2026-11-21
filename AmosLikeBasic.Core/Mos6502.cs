using System;

namespace AmosLikeBasic;

// What the 6502 reads and writes: the C64's memory and its chips
public interface IMos6502Bus
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
}

// The MOS 6502 (the 6510 of the Commodore 64): A, X, Y, the stack in page 1 and the status flags.
// All 256 opcodes as the NMOS chip runs them, the undocumented ones too, since music players use
// them (LAX, SAX, DCP, ISC and the rest), with their cycle counts. A read-modify-write writes the old
// value back first, as the chip does, which acknowledges the VIC's interrupts the way players expect.
public sealed class Mos6502
{
    public const byte C = 1, Z = 2, I = 4, D = 8, B = 16, U = 32, V = 64, N = 128;

    public byte A, X, Y, S = 0xFD, P = I | U;
    public ushort PC;
    public long Cycles;
    public bool Jammed;              // a JAM opcode stops the chip until it is reset
    private readonly IMos6502Bus _bus;

    public Mos6502(IMos6502Bus bus) => _bus = bus;

    private enum Mode { Imp, Acc, Imm, Zp, Zpx, Zpy, Abs, Abx, Aby, Ind, Izx, Izy, Rel }

    private enum Op
    {
        ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS, CLC, CLD, CLI, CLV, CMP, CPX, CPY,
        DEC, DEX, DEY, EOR, INC, INX, INY, JMP, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP, ROL,
        ROR, RTI, RTS, SBC, SEC, SED, SEI, STA, STX, STY, TAX, TAY, TSX, TXA, TXS, TYA,
        // undocumented
        SLO, RLA, SRE, RRA, SAX, LAX, DCP, ISC, ANC, ALR, ARR, SBX, LAS, ANE, LXA, SHA, SHX, SHY, TAS, JAM
    }

    // The opcode table, a row of 16 for each high nibble: name, addressing mode and cycles
    // ("*" adds one when an index crosses a page)
    private static readonly string[] Table =
    {
        "BRK imp 7|ORA izx 6|JAM imp 2|SLO izx 8|NOP zp 3|ORA zp 3|ASL zp 5|SLO zp 5|PHP imp 3|ORA imm 2|ASL acc 2|ANC imm 2|NOP abs 4|ORA abs 4|ASL abs 6|SLO abs 6",
        "BPL rel 2|ORA izy 5*|JAM imp 2|SLO izy 8|NOP zpx 4|ORA zpx 4|ASL zpx 6|SLO zpx 6|CLC imp 2|ORA aby 4*|NOP imp 2|SLO aby 7|NOP abx 4*|ORA abx 4*|ASL abx 7|SLO abx 7",
        "JSR abs 6|AND izx 6|JAM imp 2|RLA izx 8|BIT zp 3|AND zp 3|ROL zp 5|RLA zp 5|PLP imp 4|AND imm 2|ROL acc 2|ANC imm 2|BIT abs 4|AND abs 4|ROL abs 6|RLA abs 6",
        "BMI rel 2|AND izy 5*|JAM imp 2|RLA izy 8|NOP zpx 4|AND zpx 4|ROL zpx 6|RLA zpx 6|SEC imp 2|AND aby 4*|NOP imp 2|RLA aby 7|NOP abx 4*|AND abx 4*|ROL abx 7|RLA abx 7",
        "RTI imp 6|EOR izx 6|JAM imp 2|SRE izx 8|NOP zp 3|EOR zp 3|LSR zp 5|SRE zp 5|PHA imp 3|EOR imm 2|LSR acc 2|ALR imm 2|JMP abs 3|EOR abs 4|LSR abs 6|SRE abs 6",
        "BVC rel 2|EOR izy 5*|JAM imp 2|SRE izy 8|NOP zpx 4|EOR zpx 4|LSR zpx 6|SRE zpx 6|CLI imp 2|EOR aby 4*|NOP imp 2|SRE aby 7|NOP abx 4*|EOR abx 4*|LSR abx 7|SRE abx 7",
        "RTS imp 6|ADC izx 6|JAM imp 2|RRA izx 8|NOP zp 3|ADC zp 3|ROR zp 5|RRA zp 5|PLA imp 4|ADC imm 2|ROR acc 2|ARR imm 2|JMP ind 5|ADC abs 4|ROR abs 6|RRA abs 6",
        "BVS rel 2|ADC izy 5*|JAM imp 2|RRA izy 8|NOP zpx 4|ADC zpx 4|ROR zpx 6|RRA zpx 6|SEI imp 2|ADC aby 4*|NOP imp 2|RRA aby 7|NOP abx 4*|ADC abx 4*|ROR abx 7|RRA abx 7",
        "NOP imm 2|STA izx 6|NOP imm 2|SAX izx 6|STY zp 3|STA zp 3|STX zp 3|SAX zp 3|DEY imp 2|NOP imm 2|TXA imp 2|ANE imm 2|STY abs 4|STA abs 4|STX abs 4|SAX abs 4",
        "BCC rel 2|STA izy 6|JAM imp 2|SHA izy 6|STY zpx 4|STA zpx 4|STX zpy 4|SAX zpy 4|TYA imp 2|STA aby 5|TXS imp 2|TAS aby 5|SHY abx 5|STA abx 5|SHX aby 5|SHA aby 5",
        "LDY imm 2|LDA izx 6|LDX imm 2|LAX izx 6|LDY zp 3|LDA zp 3|LDX zp 3|LAX zp 3|TAY imp 2|LDA imm 2|TAX imp 2|LXA imm 2|LDY abs 4|LDA abs 4|LDX abs 4|LAX abs 4",
        "BCS rel 2|LDA izy 5*|JAM imp 2|LAX izy 5*|LDY zpx 4|LDA zpx 4|LDX zpy 4|LAX zpy 4|CLV imp 2|LDA aby 4*|TSX imp 2|LAS aby 4*|LDY abx 4*|LDA abx 4*|LDX aby 4*|LAX aby 4*",
        "CPY imm 2|CMP izx 6|NOP imm 2|DCP izx 8|CPY zp 3|CMP zp 3|DEC zp 5|DCP zp 5|INY imp 2|CMP imm 2|DEX imp 2|SBX imm 2|CPY abs 4|CMP abs 4|DEC abs 6|DCP abs 6",
        "BNE rel 2|CMP izy 5*|JAM imp 2|DCP izy 8|NOP zpx 4|CMP zpx 4|DEC zpx 6|DCP zpx 6|CLD imp 2|CMP aby 4*|NOP imp 2|DCP aby 7|NOP abx 4*|CMP abx 4*|DEC abx 7|DCP abx 7",
        "CPX imm 2|SBC izx 6|NOP imm 2|ISC izx 8|CPX zp 3|SBC zp 3|INC zp 5|ISC zp 5|INX imp 2|SBC imm 2|NOP imp 2|SBC imm 2|CPX abs 4|SBC abs 4|INC abs 6|ISC abs 6",
        "BEQ rel 2|SBC izy 5*|JAM imp 2|ISC izy 8|NOP zpx 4|SBC zpx 4|INC zpx 6|ISC zpx 6|SED imp 2|SBC aby 4*|NOP imp 2|ISC aby 7|NOP abx 4*|SBC abx 4*|INC abx 7|ISC abx 7",
    };

    private static readonly Op[] Ops = new Op[256];
    private static readonly Mode[] Modes = new Mode[256];
    private static readonly byte[] Times = new byte[256];
    private static readonly bool[] PageCross = new bool[256];

    static Mos6502()
    {
        for (int row = 0; row < 16; row++)
        {
            var cells = Table[row].Split('|');
            for (int col = 0; col < 16; col++)
            {
                var parts = cells[col].Split(' ');
                int op = row * 16 + col;
                Ops[op] = Enum.Parse<Op>(parts[0]);
                Modes[op] = Enum.Parse<Mode>(parts[1], ignoreCase: true);
                PageCross[op] = parts[2].EndsWith('*');
                Times[op] = byte.Parse(parts[2].TrimEnd('*'));
            }
        }
    }

    // ── Memory and the stack ──

    private byte Read(int a) => _bus.Read((ushort)a);
    private void Write(int a, int v) => _bus.Write((ushort)a, (byte)v);
    private ushort Read16(int a) => (ushort)(Read(a) | Read((a + 1) & 0xFFFF) << 8);
    private byte Fetch() => Read(PC++);
    private ushort Fetch16() { ushort w = Read16(PC); PC += 2; return w; }

    public void Push(int v) { Write(0x100 | S, v); S--; }
    public byte Pull() { S++; return Read(0x100 | S); }
    public void Push16(int v) { Push(v >> 8); Push(v & 0xFF); }
    public ushort Pull16() { int lo = Pull(); return (ushort)(lo | Pull() << 8); }

    private void Flag(byte f, bool on) => P = (byte)(on ? P | f : P & ~f);
    private void NZ(int v) { Flag(Z, (v & 0xFF) == 0); Flag(N, (v & 0x80) != 0); }

    // ── Interrupts ──

    // An IRQ is taken if I is clear; returns whether it was. An NMI always is.
    public bool Irq()
    {
        if ((P & I) != 0) return false;
        Interrupt(0xFFFE);
        return true;
    }

    public void Nmi() => Interrupt(0xFFFA);

    private void Interrupt(int vector)
    {
        Push16(PC);
        Push((P & ~B) | U);
        P |= I;
        PC = Read16(vector);
        Cycles += 7;
        Jammed = false;
    }

    // ── Running ──

    // One instruction
    public void Step()
    {
        if (Jammed) { Cycles += 2; return; }
        int opcode = Fetch();
        var mode = Modes[opcode];
        Cycles += Times[opcode];

        // Where the operand is (for all but implied, accumulator and immediate)
        int address = 0;
        bool crossed = false;
        switch (mode)
        {
            case Mode.Imm: address = PC++; break;
            case Mode.Zp: address = Fetch(); break;
            case Mode.Zpx: address = (Fetch() + X) & 0xFF; break;
            case Mode.Zpy: address = (Fetch() + Y) & 0xFF; break;
            case Mode.Abs: address = Fetch16(); break;
            case Mode.Abx: { int b = Fetch16(); address = (b + X) & 0xFFFF; crossed = (b & 0xFF00) != (address & 0xFF00); break; }
            case Mode.Aby: { int b = Fetch16(); address = (b + Y) & 0xFFFF; crossed = (b & 0xFF00) != (address & 0xFF00); break; }
            case Mode.Ind:
            {
                // The 6502's bug: a pointer at the end of a page takes its high byte from the start of it
                int p = Fetch16();
                address = Read(p) | Read((p & 0xFF00) | ((p + 1) & 0xFF)) << 8;
                break;
            }
            case Mode.Izx: { int z = (Fetch() + X) & 0xFF; address = Read(z) | Read((z + 1) & 0xFF) << 8; break; }
            case Mode.Izy:
            {
                int z = Fetch();
                int b = Read(z) | Read((z + 1) & 0xFF) << 8;
                address = (b + Y) & 0xFFFF;
                crossed = (b & 0xFF00) != (address & 0xFF00);
                break;
            }
            case Mode.Rel: address = (sbyte)Fetch(); break;
        }
        if (crossed && PageCross[opcode]) Cycles++;

        switch (Ops[opcode])
        {
            case Op.ADC: Adc(Read(address)); break;
            case Op.SBC: Sbc(Read(address)); break;
            case Op.AND: A &= Read(address); NZ(A); break;
            case Op.ORA: A |= Read(address); NZ(A); break;
            case Op.EOR: A ^= Read(address); NZ(A); break;
            case Op.CMP: Compare(A, Read(address)); break;
            case Op.CPX: Compare(X, Read(address)); break;
            case Op.CPY: Compare(Y, Read(address)); break;
            case Op.BIT:
            {
                int m = Read(address);
                Flag(Z, (A & m) == 0);
                Flag(N, (m & 0x80) != 0);
                Flag(V, (m & 0x40) != 0);
                break;
            }
            case Op.LDA: A = Read(address); NZ(A); break;
            case Op.LDX: X = Read(address); NZ(X); break;
            case Op.LDY: Y = Read(address); NZ(Y); break;
            case Op.STA: Write(address, A); break;
            case Op.STX: Write(address, X); break;
            case Op.STY: Write(address, Y); break;

            case Op.ASL: Modify(mode, address, Asl); break;
            case Op.LSR: Modify(mode, address, Lsr); break;
            case Op.ROL: Modify(mode, address, Rol); break;
            case Op.ROR: Modify(mode, address, Ror); break;
            case Op.INC: Modify(mode, address, v => { v = (v + 1) & 0xFF; NZ(v); return v; }); break;
            case Op.DEC: Modify(mode, address, v => { v = (v - 1) & 0xFF; NZ(v); return v; }); break;

            case Op.INX: X++; NZ(X); break;
            case Op.INY: Y++; NZ(Y); break;
            case Op.DEX: X--; NZ(X); break;
            case Op.DEY: Y--; NZ(Y); break;
            case Op.TAX: X = A; NZ(X); break;
            case Op.TAY: Y = A; NZ(Y); break;
            case Op.TXA: A = X; NZ(A); break;
            case Op.TYA: A = Y; NZ(A); break;
            case Op.TSX: X = S; NZ(X); break;
            case Op.TXS: S = X; break;

            case Op.PHA: Push(A); break;
            case Op.PHP: Push(P | B | U); break;
            case Op.PLA: A = Pull(); NZ(A); break;
            case Op.PLP: P = (byte)((Pull() & ~B) | U); break;

            case Op.CLC: P &= unchecked((byte)~C); break;
            case Op.SEC: P |= C; break;
            case Op.CLI: P &= unchecked((byte)~I); break;
            case Op.SEI: P |= I; break;
            case Op.CLD: P &= unchecked((byte)~D); break;
            case Op.SED: P |= D; break;
            case Op.CLV: P &= unchecked((byte)~V); break;

            case Op.BPL: Branch((P & N) == 0, address); break;
            case Op.BMI: Branch((P & N) != 0, address); break;
            case Op.BVC: Branch((P & V) == 0, address); break;
            case Op.BVS: Branch((P & V) != 0, address); break;
            case Op.BCC: Branch((P & C) == 0, address); break;
            case Op.BCS: Branch((P & C) != 0, address); break;
            case Op.BNE: Branch((P & Z) == 0, address); break;
            case Op.BEQ: Branch((P & Z) != 0, address); break;

            case Op.JMP: PC = (ushort)address; break;
            case Op.JSR: Push16(PC - 1); PC = (ushort)address; break;
            case Op.RTS: PC = (ushort)(Pull16() + 1); break;
            case Op.RTI: P = (byte)((Pull() & ~B) | U); PC = Pull16(); break;
            case Op.BRK:
                PC++;
                Push16(PC);
                Push(P | B | U);
                P |= I;
                PC = Read16(0xFFFE);
                break;
            case Op.NOP:
                if (mode is not (Mode.Imp or Mode.Imm)) Read(address);
                break;

            // ── Undocumented ──
            case Op.SLO: Modify(mode, address, v => { v = Asl(v); A |= (byte)v; NZ(A); return v; }); break;
            case Op.RLA: Modify(mode, address, v => { v = Rol(v); A &= (byte)v; NZ(A); return v; }); break;
            case Op.SRE: Modify(mode, address, v => { v = Lsr(v); A ^= (byte)v; NZ(A); return v; }); break;
            case Op.RRA: Modify(mode, address, v => { v = Ror(v); Adc(v); return v; }); break;
            case Op.DCP: Modify(mode, address, v => { v = (v - 1) & 0xFF; Compare(A, v); return v; }); break;
            case Op.ISC: Modify(mode, address, v => { v = (v + 1) & 0xFF; Sbc(v); return v; }); break;
            case Op.SAX: Write(address, A & X); break;
            case Op.LAX: A = X = Read(address); NZ(A); break;
            case Op.ANC: A &= Read(address); NZ(A); Flag(C, (A & 0x80) != 0); break;
            case Op.ALR: A &= Read(address); A = (byte)Lsr(A); break;
            case Op.ARR:
                A &= Read(address);
                A = (byte)(A >> 1 | (P & C) << 7);
                NZ(A);
                Flag(C, (A & 0x40) != 0);
                Flag(V, (((A >> 6) ^ (A >> 5)) & 1) != 0);
                break;
            case Op.SBX:
            {
                int r = (A & X) - Read(address);
                Flag(C, r >= 0);
                X = (byte)r;
                NZ(X);
                break;
            }
            case Op.LAS: A = X = S = (byte)(Read(address) & S); NZ(A); break;
            case Op.ANE: A = (byte)((A | 0xEE) & X & Read(address)); NZ(A); break;
            case Op.LXA: A = X = (byte)((A | 0xEE) & Read(address)); NZ(A); break;
            // Stores of a register ANDed with the high byte of the address plus one
            case Op.SHA: Write(address, A & X & ((address >> 8) + 1)); break;
            case Op.SHX: Write(address, X & ((address >> 8) + 1)); break;
            case Op.SHY: Write(address, Y & ((address >> 8) + 1)); break;
            case Op.TAS: S = (byte)(A & X); Write(address, S & ((address >> 8) + 1)); break;
            case Op.JAM: Jammed = true; PC--; break;
        }
    }

    private void Branch(bool taken, int offset)
    {
        if (!taken) return;
        int target = (PC + offset) & 0xFFFF;
        Cycles += (target & 0xFF00) != (PC & 0xFF00) ? 2 : 1;
        PC = (ushort)target;
    }

    // A read-modify-write: on the accumulator, or in memory with the old value written back first
    private void Modify(Mode mode, int address, Func<int, int> change)
    {
        if (mode == Mode.Acc) { A = (byte)change(A); return; }
        int old = Read(address);
        Write(address, old);
        Write(address, change(old));
    }

    private int Asl(int v) { Flag(C, (v & 0x80) != 0); v = (v << 1) & 0xFF; NZ(v); return v; }
    private int Lsr(int v) { Flag(C, (v & 1) != 0); v >>= 1; NZ(v); return v; }
    private int Rol(int v) { int c = P & C; Flag(C, (v & 0x80) != 0); v = ((v << 1) | c) & 0xFF; NZ(v); return v; }
    private int Ror(int v) { int c = P & C; Flag(C, (v & 1) != 0); v = (v >> 1) | c << 7; NZ(v); return v; }

    private void Compare(int register, int m)
    {
        int r = register - m;
        Flag(C, r >= 0);
        NZ(r);
    }

    // Add with carry; in decimal mode as the NMOS chip does it (N and V from the sum before the
    // high digit is corrected, Z from the binary sum)
    private void Adc(int m)
    {
        int c = P & C;
        int sum = A + m + c;
        if ((P & D) == 0)
        {
            Flag(C, sum > 0xFF);
            Flag(V, (~(A ^ m) & (A ^ sum) & 0x80) != 0);
            A = (byte)sum;
            NZ(A);
            return;
        }
        int lo = (A & 0x0F) + (m & 0x0F) + c;
        if (lo >= 0x0A) lo = ((lo + 0x06) & 0x0F) + 0x10;
        int r = (A & 0xF0) + (m & 0xF0) + lo;
        Flag(Z, (sum & 0xFF) == 0);
        Flag(N, (r & 0x80) != 0);
        Flag(V, (~(A ^ m) & (A ^ r) & 0x80) != 0);
        if (r >= 0xA0) r += 0x60;
        Flag(C, r >= 0x100);
        A = (byte)r;
    }

    // Subtract with borrow; the flags as in binary in both modes
    private void Sbc(int m)
    {
        int c = P & C;
        int diff = A - m - (1 - c);
        Flag(C, diff >= 0);
        Flag(V, ((A ^ m) & (A ^ diff) & 0x80) != 0);
        int result = diff & 0xFF;
        if ((P & D) != 0)
        {
            int lo = (A & 0x0F) - (m & 0x0F) + c - 1;
            if (lo < 0) lo = ((lo - 0x06) & 0x0F) - 0x10;
            int r = (A & 0xF0) - (m & 0xF0) + lo;
            if (r < 0) r -= 0x60;
            NZ(result);
            A = (byte)r;
            return;
        }
        A = (byte)result;
        NZ(A);
    }
}
