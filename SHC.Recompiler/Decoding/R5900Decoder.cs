namespace SHC.Recompiler.Decoding;

/// <summary>
/// Decodificador de instruções do Emotion Engine (R5900).
/// Cobre MIPS III, as extensões MMI, o FPU (COP1, precisão simples) e as
/// transferências do COP2. As instruções de macro do VU0 são apenas
/// identificadas (Opcode.Vu0_Macro) e serão detalhadas depois.
/// As tabelas foram verificadas contra o executável e os overlays do jogo.
/// </summary>
public static class R5900Decoder
{
    private readonly record struct Entry(Opcode Opcode, OperandFormat Format);

    private static readonly Entry Unknown =
        new(Opcode.Unknown, OperandFormat.Unknown);

    private static readonly Entry Vu0Macro =
        new(Opcode.Vu0_Macro, OperandFormat.Vu0Macro);

    /// <summary>
    /// Decodifica uma palavra de 32 bits localizada em <paramref name="address"/>.
    /// Palavras que não são instruções válidas retornam Opcode.Unknown.
    /// </summary>
    public static Instruction Decode(uint address, uint word)
    {
        uint op = word >> 26;
        uint rs = (word >> 21) & 0x1F;
        uint rt = (word >> 16) & 0x1F;
        uint sa = (word >> 6) & 0x1F;
        uint funct = word & 0x3F;

        Entry entry = op switch
        {
            0x00 => Special(funct),
            0x01 => Regimm(rt),
            0x10 => DecodeCop0(rs, rt, funct),
            0x11 => DecodeCop1(rs, rt, funct),
            0x12 => DecodeCop2(rs, rt),
            0x1C => DecodeMmi(sa, funct),
            _ => Primary(op)
        };

        return new Instruction(address, word, entry.Opcode, entry.Format);
    }

    private static Entry DecodeCop0(uint rs, uint rt, uint funct) => rs switch
    {
        0x00 or 0x04 => Cop0Move(rs),
        0x08 => Cop0Branch(rt),
        0x10 => Cop0Co(funct),
        _ => Unknown
    };

    private static Entry DecodeCop1(uint rs, uint rt, uint funct) => rs switch
    {
        0x00 or 0x02 or 0x04 or 0x06 => Cop1Move(rs),
        0x08 => Cop1Branch(rt),
        0x10 => Cop1S(funct),
        0x14 => Cop1W(funct),
        _ => Unknown
    };

    private static Entry DecodeCop2(uint rs, uint rt) => rs switch
    {
        0x01 or 0x02 or 0x05 or 0x06 => Cop2Move(rs),
        0x08 => Cop2Branch(rt),
        >= 0x10 => Vu0Macro,
        _ => Unknown
    };

    private static Entry DecodeMmi(uint sa, uint funct) => funct switch
    {
        0x08 => Mmi0(sa),
        0x28 => Mmi1(sa),
        0x09 => Mmi2(sa),
        0x29 => Mmi3(sa),
        0x30 => Pmfhl(sa),
        0x31 => Pmthl(sa),
        _ => Mmi(funct)
    };

    // Opcode primário (bits 31-26).
    private static Entry Primary(uint index) => index switch
    {
        0x02 => new(Opcode.J, OperandFormat.Jump),
        0x03 => new(Opcode.Jal, OperandFormat.Jump),
        0x04 => new(Opcode.Beq, OperandFormat.RsRtBranch),
        0x05 => new(Opcode.Bne, OperandFormat.RsRtBranch),
        0x06 => new(Opcode.Blez, OperandFormat.RsBranch),
        0x07 => new(Opcode.Bgtz, OperandFormat.RsBranch),
        0x08 => new(Opcode.Addi, OperandFormat.RtRsImm),
        0x09 => new(Opcode.Addiu, OperandFormat.RtRsImm),
        0x0A => new(Opcode.Slti, OperandFormat.RtRsImm),
        0x0B => new(Opcode.Sltiu, OperandFormat.RtRsImm),
        0x0C => new(Opcode.Andi, OperandFormat.RtRsImm),
        0x0D => new(Opcode.Ori, OperandFormat.RtRsImm),
        0x0E => new(Opcode.Xori, OperandFormat.RtRsImm),
        0x0F => new(Opcode.Lui, OperandFormat.RtImm),
        0x14 => new(Opcode.Beql, OperandFormat.RsRtBranch),
        0x15 => new(Opcode.Bnel, OperandFormat.RsRtBranch),
        0x16 => new(Opcode.Blezl, OperandFormat.RsBranch),
        0x17 => new(Opcode.Bgtzl, OperandFormat.RsBranch),
        0x18 => new(Opcode.Daddi, OperandFormat.RtRsImm),
        0x19 => new(Opcode.Daddiu, OperandFormat.RtRsImm),
        0x1A => new(Opcode.Ldl, OperandFormat.RtMem),
        0x1B => new(Opcode.Ldr, OperandFormat.RtMem),
        0x1E => new(Opcode.Lq, OperandFormat.RtMem),
        0x1F => new(Opcode.Sq, OperandFormat.RtMem),
        0x20 => new(Opcode.Lb, OperandFormat.RtMem),
        0x21 => new(Opcode.Lh, OperandFormat.RtMem),
        0x22 => new(Opcode.Lwl, OperandFormat.RtMem),
        0x23 => new(Opcode.Lw, OperandFormat.RtMem),
        0x24 => new(Opcode.Lbu, OperandFormat.RtMem),
        0x25 => new(Opcode.Lhu, OperandFormat.RtMem),
        0x26 => new(Opcode.Lwr, OperandFormat.RtMem),
        0x27 => new(Opcode.Lwu, OperandFormat.RtMem),
        0x28 => new(Opcode.Sb, OperandFormat.RtMem),
        0x29 => new(Opcode.Sh, OperandFormat.RtMem),
        0x2A => new(Opcode.Swl, OperandFormat.RtMem),
        0x2B => new(Opcode.Sw, OperandFormat.RtMem),
        0x2C => new(Opcode.Sdl, OperandFormat.RtMem),
        0x2D => new(Opcode.Sdr, OperandFormat.RtMem),
        0x2E => new(Opcode.Swr, OperandFormat.RtMem),
        0x2F => new(Opcode.Cache, OperandFormat.CacheOp),
        0x31 => new(Opcode.Lwc1, OperandFormat.FtMem),
        0x33 => new(Opcode.Pref, OperandFormat.CacheOp),
        0x36 => new(Opcode.Lqc2, OperandFormat.VfMem),
        0x37 => new(Opcode.Ld, OperandFormat.RtMem),
        0x39 => new(Opcode.Swc1, OperandFormat.FtMem),
        0x3E => new(Opcode.Sqc2, OperandFormat.VfMem),
        0x3F => new(Opcode.Sd, OperandFormat.RtMem),
        _ => Unknown
    };

    // SPECIAL: campo funct (bits 5-0).
    private static Entry Special(uint index) => index switch
    {
        0x00 => new(Opcode.Sll, OperandFormat.RdRtSa),
        0x02 => new(Opcode.Srl, OperandFormat.RdRtSa),
        0x03 => new(Opcode.Sra, OperandFormat.RdRtSa),
        0x04 => new(Opcode.Sllv, OperandFormat.RdRtRs),
        0x06 => new(Opcode.Srlv, OperandFormat.RdRtRs),
        0x07 => new(Opcode.Srav, OperandFormat.RdRtRs),
        0x08 => new(Opcode.Jr, OperandFormat.Rs),
        0x09 => new(Opcode.Jalr, OperandFormat.Jalr),
        0x0A => new(Opcode.Movz, OperandFormat.RdRsRt),
        0x0B => new(Opcode.Movn, OperandFormat.RdRsRt),
        0x0C => new(Opcode.Syscall, OperandFormat.Code),
        0x0D => new(Opcode.Break, OperandFormat.Code),
        0x0F => new(Opcode.Sync, OperandFormat.None),
        0x10 => new(Opcode.Mfhi, OperandFormat.Rd),
        0x11 => new(Opcode.Mthi, OperandFormat.Rs),
        0x12 => new(Opcode.Mflo, OperandFormat.Rd),
        0x13 => new(Opcode.Mtlo, OperandFormat.Rs),
        0x14 => new(Opcode.Dsllv, OperandFormat.RdRtRs),
        0x16 => new(Opcode.Dsrlv, OperandFormat.RdRtRs),
        0x17 => new(Opcode.Dsrav, OperandFormat.RdRtRs),
        0x18 => new(Opcode.Mult, OperandFormat.RdRsRtOpt),
        0x19 => new(Opcode.Multu, OperandFormat.RdRsRtOpt),
        0x1A => new(Opcode.Div, OperandFormat.RsRt),
        0x1B => new(Opcode.Divu, OperandFormat.RsRt),
        0x20 => new(Opcode.Add, OperandFormat.RdRsRt),
        0x21 => new(Opcode.Addu, OperandFormat.RdRsRt),
        0x22 => new(Opcode.Sub, OperandFormat.RdRsRt),
        0x23 => new(Opcode.Subu, OperandFormat.RdRsRt),
        0x24 => new(Opcode.And, OperandFormat.RdRsRt),
        0x25 => new(Opcode.Or, OperandFormat.RdRsRt),
        0x26 => new(Opcode.Xor, OperandFormat.RdRsRt),
        0x27 => new(Opcode.Nor, OperandFormat.RdRsRt),
        0x28 => new(Opcode.Mfsa, OperandFormat.Rd),
        0x29 => new(Opcode.Mtsa, OperandFormat.Rs),
        0x2A => new(Opcode.Slt, OperandFormat.RdRsRt),
        0x2B => new(Opcode.Sltu, OperandFormat.RdRsRt),
        0x2C => new(Opcode.Dadd, OperandFormat.RdRsRt),
        0x2D => new(Opcode.Daddu, OperandFormat.RdRsRt),
        0x2E => new(Opcode.Dsub, OperandFormat.RdRsRt),
        0x2F => new(Opcode.Dsubu, OperandFormat.RdRsRt),
        0x30 => new(Opcode.Tge, OperandFormat.RsRt),
        0x31 => new(Opcode.Tgeu, OperandFormat.RsRt),
        0x32 => new(Opcode.Tlt, OperandFormat.RsRt),
        0x33 => new(Opcode.Tltu, OperandFormat.RsRt),
        0x34 => new(Opcode.Teq, OperandFormat.RsRt),
        0x36 => new(Opcode.Tne, OperandFormat.RsRt),
        0x38 => new(Opcode.Dsll, OperandFormat.RdRtSa),
        0x3A => new(Opcode.Dsrl, OperandFormat.RdRtSa),
        0x3B => new(Opcode.Dsra, OperandFormat.RdRtSa),
        0x3C => new(Opcode.Dsll32, OperandFormat.RdRtSa),
        0x3E => new(Opcode.Dsrl32, OperandFormat.RdRtSa),
        0x3F => new(Opcode.Dsra32, OperandFormat.RdRtSa),
        _ => Unknown
    };

    // REGIMM: campo rt (bits 20-16).
    private static Entry Regimm(uint index) => index switch
    {
        0x00 => new(Opcode.Bltz, OperandFormat.RsBranch),
        0x01 => new(Opcode.Bgez, OperandFormat.RsBranch),
        0x02 => new(Opcode.Bltzl, OperandFormat.RsBranch),
        0x03 => new(Opcode.Bgezl, OperandFormat.RsBranch),
        0x08 => new(Opcode.Tgei, OperandFormat.RsImm),
        0x09 => new(Opcode.Tgeiu, OperandFormat.RsImm),
        0x0A => new(Opcode.Tlti, OperandFormat.RsImm),
        0x0B => new(Opcode.Tltiu, OperandFormat.RsImm),
        0x0C => new(Opcode.Teqi, OperandFormat.RsImm),
        0x0E => new(Opcode.Tnei, OperandFormat.RsImm),
        0x10 => new(Opcode.Bltzal, OperandFormat.RsBranch),
        0x11 => new(Opcode.Bgezal, OperandFormat.RsBranch),
        0x12 => new(Opcode.Bltzall, OperandFormat.RsBranch),
        0x13 => new(Opcode.Bgezall, OperandFormat.RsBranch),
        0x18 => new(Opcode.Mtsab, OperandFormat.RsImm),
        0x19 => new(Opcode.Mtsah, OperandFormat.RsImm),
        _ => Unknown
    };

    // COP0: MFC0/MTC0, por rs.
    private static Entry Cop0Move(uint index) => index switch
    {
        0x00 => new(Opcode.Mfc0, OperandFormat.RtRd),
        0x04 => new(Opcode.Mtc0, OperandFormat.RtRd),
        _ => Unknown
    };

    // COP0: desvios BC0, por rt.
    private static Entry Cop0Branch(uint index) => index switch
    {
        0x00 => new(Opcode.Bc0f, OperandFormat.Branch),
        0x01 => new(Opcode.Bc0t, OperandFormat.Branch),
        0x02 => new(Opcode.Bc0fl, OperandFormat.Branch),
        0x03 => new(Opcode.Bc0tl, OperandFormat.Branch),
        _ => Unknown
    };

    // COP0: operações de TLB e interrupções, por funct.
    private static Entry Cop0Co(uint index) => index switch
    {
        0x01 => new(Opcode.Tlbr, OperandFormat.None),
        0x02 => new(Opcode.Tlbwi, OperandFormat.None),
        0x06 => new(Opcode.Tlbwr, OperandFormat.None),
        0x08 => new(Opcode.Tlbp, OperandFormat.None),
        0x18 => new(Opcode.Eret, OperandFormat.None),
        0x38 => new(Opcode.Ei, OperandFormat.None),
        0x39 => new(Opcode.Di, OperandFormat.None),
        _ => Unknown
    };

    // COP1: transferências MFC1/CFC1/MTC1/CTC1, por rs.
    private static Entry Cop1Move(uint index) => index switch
    {
        0x00 => new(Opcode.Mfc1, OperandFormat.RtFs),
        0x02 => new(Opcode.Cfc1, OperandFormat.RtFs),
        0x04 => new(Opcode.Mtc1, OperandFormat.RtFs),
        0x06 => new(Opcode.Ctc1, OperandFormat.RtFs),
        _ => Unknown
    };

    // COP1: desvios BC1, por rt.
    private static Entry Cop1Branch(uint index) => index switch
    {
        0x00 => new(Opcode.Bc1f, OperandFormat.Branch),
        0x01 => new(Opcode.Bc1t, OperandFormat.Branch),
        0x02 => new(Opcode.Bc1fl, OperandFormat.Branch),
        0x03 => new(Opcode.Bc1tl, OperandFormat.Branch),
        _ => Unknown
    };

    // COP1 formato S (precisão simples), por funct.
    private static Entry Cop1S(uint index) => index switch
    {
        0x00 => new(Opcode.Add_S, OperandFormat.FdFsFt),
        0x01 => new(Opcode.Sub_S, OperandFormat.FdFsFt),
        0x02 => new(Opcode.Mul_S, OperandFormat.FdFsFt),
        0x03 => new(Opcode.Div_S, OperandFormat.FdFsFt),
        0x04 => new(Opcode.Sqrt_S, OperandFormat.FdFt),
        0x05 => new(Opcode.Abs_S, OperandFormat.FdFs),
        0x06 => new(Opcode.Mov_S, OperandFormat.FdFs),
        0x07 => new(Opcode.Neg_S, OperandFormat.FdFs),
        0x16 => new(Opcode.Rsqrt_S, OperandFormat.FdFsFt),
        0x18 => new(Opcode.Adda_S, OperandFormat.FsFt),
        0x19 => new(Opcode.Suba_S, OperandFormat.FsFt),
        0x1A => new(Opcode.Mula_S, OperandFormat.FsFt),
        0x1C => new(Opcode.Madd_S, OperandFormat.FdFsFt),
        0x1D => new(Opcode.Msub_S, OperandFormat.FdFsFt),
        0x1E => new(Opcode.Madda_S, OperandFormat.FsFt),
        0x1F => new(Opcode.Msuba_S, OperandFormat.FsFt),
        0x24 => new(Opcode.Cvt_W_S, OperandFormat.FdFs),
        0x28 => new(Opcode.Max_S, OperandFormat.FdFsFt),
        0x29 => new(Opcode.Min_S, OperandFormat.FdFsFt),
        0x30 => new(Opcode.C_F_S, OperandFormat.FsFt),
        0x32 => new(Opcode.C_Eq_S, OperandFormat.FsFt),
        0x34 => new(Opcode.C_Lt_S, OperandFormat.FsFt),
        0x36 => new(Opcode.C_Le_S, OperandFormat.FsFt),
        _ => Unknown
    };

    // COP1 formato W (inteiro), por funct.
    private static Entry Cop1W(uint index) => index switch
    {
        0x20 => new(Opcode.Cvt_S_W, OperandFormat.FdFs),
        _ => Unknown
    };

    // COP2: transferências QMFC2/CFC2/QMTC2/CTC2, por rs.
    private static Entry Cop2Move(uint index) => index switch
    {
        0x01 => new(Opcode.Qmfc2, OperandFormat.RtVf),
        0x02 => new(Opcode.Cfc2, OperandFormat.RtVi),
        0x05 => new(Opcode.Qmtc2, OperandFormat.RtVf),
        0x06 => new(Opcode.Ctc2, OperandFormat.RtVi),
        _ => Unknown
    };

    // COP2: desvios BC2, por rt.
    private static Entry Cop2Branch(uint index) => index switch
    {
        0x00 => new(Opcode.Bc2f, OperandFormat.Branch),
        0x01 => new(Opcode.Bc2t, OperandFormat.Branch),
        0x02 => new(Opcode.Bc2fl, OperandFormat.Branch),
        0x03 => new(Opcode.Bc2tl, OperandFormat.Branch),
        _ => Unknown
    };

    // MMI: campo funct (bits 5-0).
    private static Entry Mmi(uint index) => index switch
    {
        0x00 => new(Opcode.Madd, OperandFormat.RdRsRtOpt),
        0x01 => new(Opcode.Maddu, OperandFormat.RdRsRtOpt),
        0x04 => new(Opcode.Plzcw, OperandFormat.RdRs),
        0x10 => new(Opcode.Mfhi1, OperandFormat.Rd),
        0x11 => new(Opcode.Mthi1, OperandFormat.Rs),
        0x12 => new(Opcode.Mflo1, OperandFormat.Rd),
        0x13 => new(Opcode.Mtlo1, OperandFormat.Rs),
        0x18 => new(Opcode.Mult1, OperandFormat.RdRsRtOpt),
        0x19 => new(Opcode.Multu1, OperandFormat.RdRsRtOpt),
        0x1A => new(Opcode.Div1, OperandFormat.RsRt),
        0x1B => new(Opcode.Divu1, OperandFormat.RsRt),
        0x20 => new(Opcode.Madd1, OperandFormat.RdRsRtOpt),
        0x21 => new(Opcode.Maddu1, OperandFormat.RdRsRtOpt),
        0x34 => new(Opcode.Psllh, OperandFormat.RdRtSa),
        0x36 => new(Opcode.Psrlh, OperandFormat.RdRtSa),
        0x37 => new(Opcode.Psrah, OperandFormat.RdRtSa),
        0x3C => new(Opcode.Psllw, OperandFormat.RdRtSa),
        0x3E => new(Opcode.Psrlw, OperandFormat.RdRtSa),
        0x3F => new(Opcode.Psraw, OperandFormat.RdRtSa),
        _ => Unknown
    };

    // MMI0: campo sa (bits 10-6).
    private static Entry Mmi0(uint index) => index switch
    {
        0x00 => new(Opcode.Paddw, OperandFormat.RdRsRt),
        0x01 => new(Opcode.Psubw, OperandFormat.RdRsRt),
        0x02 => new(Opcode.Pcgtw, OperandFormat.RdRsRt),
        0x03 => new(Opcode.Pmaxw, OperandFormat.RdRsRt),
        0x04 => new(Opcode.Paddh, OperandFormat.RdRsRt),
        0x05 => new(Opcode.Psubh, OperandFormat.RdRsRt),
        0x06 => new(Opcode.Pcgth, OperandFormat.RdRsRt),
        0x07 => new(Opcode.Pmaxh, OperandFormat.RdRsRt),
        0x08 => new(Opcode.Paddb, OperandFormat.RdRsRt),
        0x09 => new(Opcode.Psubb, OperandFormat.RdRsRt),
        0x0A => new(Opcode.Pcgtb, OperandFormat.RdRsRt),
        0x10 => new(Opcode.Paddsw, OperandFormat.RdRsRt),
        0x11 => new(Opcode.Psubsw, OperandFormat.RdRsRt),
        0x12 => new(Opcode.Pextlw, OperandFormat.RdRsRt),
        0x13 => new(Opcode.Ppacw, OperandFormat.RdRsRt),
        0x14 => new(Opcode.Paddsh, OperandFormat.RdRsRt),
        0x15 => new(Opcode.Psubsh, OperandFormat.RdRsRt),
        0x16 => new(Opcode.Pextlh, OperandFormat.RdRsRt),
        0x17 => new(Opcode.Ppach, OperandFormat.RdRsRt),
        0x18 => new(Opcode.Paddsb, OperandFormat.RdRsRt),
        0x19 => new(Opcode.Psubsb, OperandFormat.RdRsRt),
        0x1A => new(Opcode.Pextlb, OperandFormat.RdRsRt),
        0x1B => new(Opcode.Ppacb, OperandFormat.RdRsRt),
        0x1E => new(Opcode.Pext5, OperandFormat.RdRt),
        0x1F => new(Opcode.Ppac5, OperandFormat.RdRt),
        _ => Unknown
    };

    // MMI1: campo sa (bits 10-6).
    private static Entry Mmi1(uint index) => index switch
    {
        0x01 => new(Opcode.Pabsw, OperandFormat.RdRt),
        0x02 => new(Opcode.Pceqw, OperandFormat.RdRsRt),
        0x03 => new(Opcode.Pminw, OperandFormat.RdRsRt),
        0x04 => new(Opcode.Padsbh, OperandFormat.RdRsRt),
        0x05 => new(Opcode.Pabsh, OperandFormat.RdRt),
        0x06 => new(Opcode.Pceqh, OperandFormat.RdRsRt),
        0x07 => new(Opcode.Pminh, OperandFormat.RdRsRt),
        0x0A => new(Opcode.Pceqb, OperandFormat.RdRsRt),
        0x10 => new(Opcode.Padduw, OperandFormat.RdRsRt),
        0x11 => new(Opcode.Psubuw, OperandFormat.RdRsRt),
        0x12 => new(Opcode.Pextuw, OperandFormat.RdRsRt),
        0x14 => new(Opcode.Padduh, OperandFormat.RdRsRt),
        0x15 => new(Opcode.Psubuh, OperandFormat.RdRsRt),
        0x16 => new(Opcode.Pextuh, OperandFormat.RdRsRt),
        0x18 => new(Opcode.Paddub, OperandFormat.RdRsRt),
        0x19 => new(Opcode.Psubub, OperandFormat.RdRsRt),
        0x1A => new(Opcode.Pextub, OperandFormat.RdRsRt),
        0x1B => new(Opcode.Qfsrv, OperandFormat.RdRsRt),
        _ => Unknown
    };

    // MMI2: campo sa (bits 10-6).
    private static Entry Mmi2(uint index) => index switch
    {
        0x00 => new(Opcode.Pmaddw, OperandFormat.RdRsRt),
        0x02 => new(Opcode.Psllvw, OperandFormat.RdRtRs),
        0x03 => new(Opcode.Psrlvw, OperandFormat.RdRtRs),
        0x04 => new(Opcode.Pmsubw, OperandFormat.RdRsRt),
        0x08 => new(Opcode.Pmfhi, OperandFormat.Rd),
        0x09 => new(Opcode.Pmflo, OperandFormat.Rd),
        0x0A => new(Opcode.Pinth, OperandFormat.RdRsRt),
        0x0C => new(Opcode.Pmultw, OperandFormat.RdRsRt),
        0x0D => new(Opcode.Pdivw, OperandFormat.RsRt),
        0x0E => new(Opcode.Pcpyld, OperandFormat.RdRsRt),
        0x10 => new(Opcode.Pmaddh, OperandFormat.RdRsRt),
        0x11 => new(Opcode.Phmadh, OperandFormat.RdRsRt),
        0x12 => new(Opcode.Pand, OperandFormat.RdRsRt),
        0x13 => new(Opcode.Pxor, OperandFormat.RdRsRt),
        0x14 => new(Opcode.Pmsubh, OperandFormat.RdRsRt),
        0x15 => new(Opcode.Phmsbh, OperandFormat.RdRsRt),
        0x1A => new(Opcode.Pexeh, OperandFormat.RdRt),
        0x1B => new(Opcode.Prevh, OperandFormat.RdRt),
        0x1C => new(Opcode.Pmulth, OperandFormat.RdRsRt),
        0x1D => new(Opcode.Pdivbw, OperandFormat.RsRt),
        0x1E => new(Opcode.Pexew, OperandFormat.RdRt),
        0x1F => new(Opcode.Prot3w, OperandFormat.RdRt),
        _ => Unknown
    };

    // MMI3: campo sa (bits 10-6).
    private static Entry Mmi3(uint index) => index switch
    {
        0x00 => new(Opcode.Pmadduw, OperandFormat.RdRsRt),
        0x03 => new(Opcode.Psravw, OperandFormat.RdRtRs),
        0x08 => new(Opcode.Pmthi, OperandFormat.Rs),
        0x09 => new(Opcode.Pmtlo, OperandFormat.Rs),
        0x0A => new(Opcode.Pinteh, OperandFormat.RdRsRt),
        0x0C => new(Opcode.Pmultuw, OperandFormat.RdRsRt),
        0x0D => new(Opcode.Pdivuw, OperandFormat.RsRt),
        0x0E => new(Opcode.Pcpyud, OperandFormat.RdRsRt),
        0x12 => new(Opcode.Por, OperandFormat.RdRsRt),
        0x13 => new(Opcode.Pnor, OperandFormat.RdRsRt),
        0x1A => new(Opcode.Pexch, OperandFormat.RdRt),
        0x1B => new(Opcode.Pcpyh, OperandFormat.RdRt),
        0x1E => new(Opcode.Pexcw, OperandFormat.RdRt),
        _ => Unknown
    };

    // PMFHL: o campo sa escolhe a variante.
    private static Entry Pmfhl(uint index) => index switch
    {
        0x00 => new(Opcode.Pmfhl_Lw, OperandFormat.Rd),
        0x01 => new(Opcode.Pmfhl_Uw, OperandFormat.Rd),
        0x02 => new(Opcode.Pmfhl_Slw, OperandFormat.Rd),
        0x03 => new(Opcode.Pmfhl_Lh, OperandFormat.Rd),
        0x04 => new(Opcode.Pmfhl_Sh, OperandFormat.Rd),
        _ => Unknown
    };

    // PMTHL: o campo sa escolhe a variante.
    private static Entry Pmthl(uint index) => index switch
    {
        0x00 => new(Opcode.Pmthl_Lw, OperandFormat.Rs),
        _ => Unknown
    };
}
