namespace SHC.Recompiler.Decoding;

/// <summary>
/// Forma dos operandos de uma instrução. Define como os campos da palavra
/// (rs, rt, rd, sa, imediato) devem ser lidos e exibidos.
/// </summary>
public enum OperandFormat
{
    /// <summary>Sem formato (palavra desconhecida).</summary>
    Unknown = 0,

    /// <summary>Instrução de macro do VU0: mostrada como palavra crua.</summary>
    Vu0Macro,

    /// <summary>alvo do salto (J/JAL)</summary>
    Jump,

    /// <summary>rs, rt, alvo do desvio</summary>
    RsRtBranch,

    /// <summary>rs, alvo do desvio</summary>
    RsBranch,

    /// <summary>rt, rs, imediato</summary>
    RtRsImm,

    /// <summary>rt, imediato</summary>
    RtImm,

    /// <summary>rt, deslocamento(base)</summary>
    RtMem,

    /// <summary>operação (rt), deslocamento(base)</summary>
    CacheOp,

    /// <summary>ft, deslocamento(base)</summary>
    FtMem,

    /// <summary>vf, deslocamento(base)</summary>
    VfMem,

    /// <summary>rd, rt, sa</summary>
    RdRtSa,

    /// <summary>rd, rt, rs</summary>
    RdRtRs,

    /// <summary>rs</summary>
    Rs,

    /// <summary>rs ou rd, rs (rd omitido quando for $ra)</summary>
    Jalr,

    /// <summary>rd, rs, rt</summary>
    RdRsRt,

    /// <summary>código de syscall/break</summary>
    Code,

    /// <summary>Sem operandos.</summary>
    None,

    /// <summary>rd</summary>
    Rd,

    /// <summary>rd, rs, rt (R5900: rd é opcional em mult/madd; omitido quando for $zero)</summary>
    RdRsRtOpt,

    /// <summary>rs, rt</summary>
    RsRt,

    /// <summary>rs, imediato</summary>
    RsImm,

    /// <summary>rt, registrador do COP0</summary>
    RtRd,

    /// <summary>alvo do desvio</summary>
    Branch,

    /// <summary>rt, fs</summary>
    RtFs,

    /// <summary>fd, fs, ft</summary>
    FdFsFt,

    /// <summary>fd, ft</summary>
    FdFt,

    /// <summary>fd, fs</summary>
    FdFs,

    /// <summary>fs, ft</summary>
    FsFt,

    /// <summary>rt, vf (rd)</summary>
    RtVf,

    /// <summary>rt, vi (rd)</summary>
    RtVi,

    /// <summary>rd, rs</summary>
    RdRs,

    /// <summary>rd, rt</summary>
    RdRt,

}
