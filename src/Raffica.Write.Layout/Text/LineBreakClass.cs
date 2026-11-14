namespace Raffica.Write.Layout;

/// <summary>
/// The UAX #14 line break classes after rule LB1 has resolved AI, SG, XX, SA and CJ. The numbers are stored in the generated
/// table (<c>LineBreakData</c>) and <c>tools/gen-linebreak.cs</c> reads them from this file, so a class is added here with the
/// next free number and the table is regenerated. <c>QuPi</c> and <c>QuPf</c> are the quotation marks whose general category is
/// Pi or Pf (rules LB15a, LB15b and LB19 tell them apart); every rule that says QU means all three.
/// </summary>
internal enum LineBreakClass : byte
{
    Al = 0,
    Ak = 1,
    Ap = 2,
    As = 3,
    B2 = 4,
    Ba = 5,
    Bb = 6,
    Bk = 7,
    Cb = 8,
    Cl = 9,
    Cm = 10,
    Cp = 11,
    Cr = 12,
    Eb = 13,
    Em = 14,
    Ex = 15,
    Gl = 16,
    H2 = 17,
    H3 = 18,
    Hh = 19,
    Hl = 20,
    Hy = 21,
    Id = 22,
    In = 23,
    Is = 24,
    Jl = 25,
    Jt = 26,
    Jv = 27,
    Lf = 28,
    Nl = 29,
    Ns = 30,
    Nu = 31,
    Op = 32,
    Po = 33,
    Pr = 34,
    Qu = 35,
    QuPi = 36,
    QuPf = 37,
    Ri = 38,
    Sp = 39,
    Sy = 40,
    Vf = 41,
    Vi = 42,
    Wj = 43,
    Zw = 44,
    Zwj = 45,
}
