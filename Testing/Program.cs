// lomont investigate posits
using System.Diagnostics;
using System.Text.RegularExpressions;
using Lomont;

// single op tests to help debug
//SingleTest();
//SingleTestT(new Posit(32,2), "00000000000000001100111100010111", "00000000000000000000000000000011", "01111111111111110000111100010111", "/");
//return;


// where files are relative to exe
var path = @"..\..\..\TestData\";
Check8Tbl(path+"Posit8ops.txt");

Check(path+"bits16ops.txt",16,false);
Check(path+"bits32ops.txt", 32, false);

// roundtrip and value of some posits
RoundTrip(Posit.Posit8(), path+"Posit8_0.txt");
RoundTrip(Posit.Posit16(), path + "Posit16_1.txt");
RoundTrip(new Posit(16,3), path + "Posit16_3.txt");


return;

void ShowError(Posit lhs, Posit rhs, Posit truth, Posit ans, double floatval, string op)
{
    Console.WriteLine($"{lhs}({lhs.Bitstring}) {op} {rhs}({rhs.Bitstring}) = {truth}({truth.Bitstring}) obtained {ans}({ans.Bitstring}) (exact float {floatval})");
    Console.WriteLine($"dist to truth {Math.Abs(truth.Value - floatval)}, dist to obtained {Math.Abs(ans.Value - floatval)}");
}

void SingleTest(Posit ps, ulong lhsBits, ulong rhsBits, ulong truthBits, string op)
{
    // //bits for POsit16 32495 / 21 = 32767

    // 00010000 + 01000000 = 01000000: FAIL (truth 01000000 = 1, obtained 01001000 = 1.25
    // Testing 00000011 * 00110110 = 00000010:
    // ERROR: 0.015625(00000001) + 1.03125(01000001) != 1.03125(01000001) (obtained 1.0625(01000010))

    //FAIL(truth P'268435456, obtained P'NaN) for line 32783 * 33151 = 32767
    //P '-7.5(10001001) * P' - 24(10000011) = P'64(01111111) obtained P' - 32(10000010)
    // P'0.015625(00000001) * P'0.5(00100000) = P'0.015625(00000001) obtained P'0(00000000)

    var lhs = new Posit(ps); // all same size
    var rhs = new Posit(ps);
    var truth = new Posit(ps);
    lhs.Bits = lhsBits;
    rhs.Bits = rhsBits;
    truth.Bits = truthBits;
    var (ans, fv) = op switch
    {
        "+" => (lhs + rhs, lhs.Value + rhs.Value),
        "-" => (lhs - rhs, lhs.Value - rhs.Value),
        "*" => (lhs * rhs, lhs.Value * rhs.Value),
        "/" => (lhs / rhs, lhs.Value / rhs.Value),
        _ => throw new NotImplementedException($"Invalid op {op}")
    };

    ShowError(lhs, rhs, truth, ans, fv, op);
}

void SingleTestT(Posit ps, string lhsText, string rhsText, string truthText, string op)
{
    var binary = Regex.IsMatch(lhsText, "[01]+");
    var lhs   = binary ? Convert.ToUInt64(lhsText, 2) : UInt64.Parse(lhsText);
    var rhs   = binary ? Convert.ToUInt64(rhsText, 2) : UInt64.Parse(rhsText);
    var truth = binary ? Convert.ToUInt64(truthText, 2) : UInt64.Parse(truthText);
    SingleTest(ps,lhs,rhs,truth,op);
}


// Check posit(8,0) by tables (roundtrip, binary ops)
void Check8Tbl(string filename)
{
    Console.WriteLine("Checking Posit(8,0) via tables");
    var last = "";
    foreach (var line in File.ReadAllLines(filename))
    {
        if (line.Trim().StartsWith("#"))
        {
            last = line.Trim();
            continue;
        }

        var ints = line.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Int64.Parse(t))
            .ToList();

        CheckOne(ints, "add", (a, b) => a + b, (a, b) => a + b, "+");
        CheckOne(ints, "sub", (a, b) => a - b, (a, b) => a - b, "-");
        CheckOne(ints, "mul", (a, b) => a * b, (a, b) => a * b, "*");
        CheckOne(ints, "div", (a, b) => a / b, (a, b) => a / b, "/");
        CheckOne(ints, "sqrt", (a, b) => MathP.Sqrt(a), (a, b) => Math.Sqrt(a), "sqrt");

        if (last.Contains("float32"))
        {
            for (var i = 0; i < 256; ++i)
            {
                var pi = new Posit(8, 0);
                pi.Bits = (UInt32) i;
                var f1 = (float) pi.Value;
                var f2 = BitConverter.UInt32BitsToSingle((UInt32) ints[i]);
                var same = f1 == f2;
                same |= float.IsNaN(f1) && float.IsNaN(f2);
                if (!same)
                    Console.WriteLine($"ERROR at index {i}: {f1} != {f2}");
            }

            Console.WriteLine(" ...float checked");
        }

        Console.WriteLine("... done");
    }


void CheckOne(IList<long> ints, string name, Func<Posit,Posit,Posit> op, Func<double,double,double> fop, string opSym)
    {
        if (last.Contains(name))
        {
            bool singleVar = ints.Count == 256;

            var pi = new Posit(8, 0);
            var pj = new Posit(8, 0);
            var truth = new Posit(8, 0);

            var errors = 0;
            var jmax = singleVar ? 1 : 256;
            for (var i = 0; i < 256; ++i)
            for (var j = 0; j < jmax; ++j)
            {
                pi.Bits = (ulong)i;
                pj.Bits = (ulong)j;
                var index = (i * jmax + j);
                truth.Bits = (ulong)(ints[index] & 0xff);
                var ans = op(pi,pj);
                //Console.Write($"{ans.Bits},");
                var same = ans == truth;
                same |= ans.IsNaR && truth.IsNaR;
                if (!same)
                {
                    ShowError(pi, pj, truth, ans, fop(pi.Value, pj.Value), opSym);
                    //Console.WriteLine($"ERROR: {pi} + {pj} != {truth} (obtained {pi + pj})");
                    ++errors;
                }
                else
                {
                    // Console.WriteLine($"SUCCESS: {pi} + {pj} != {truth} (obtained {pi + pj})");
                }
            }
            Console.WriteLine($" ... p8 {name} checked, {errors} errors");
        }
    }
}

// parse results in file and check
void Check(string filename, int bitLen, bool isBinary)
{
    Console.WriteLine($"Checking {filename}");
    Trace.Assert(File.Exists(filename));
    int errors = 0, success = 0;
    foreach (var line in File.ReadAllLines(filename))
    {
//        Console.Write($"Testing {line}: ");
        if (line.Trim().StartsWith("#"))
            continue;
        var l2 = line; //"32495 / 21 = 32767";
        var w = l2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (w.Length == 3)
        {
            var p1 = Parse(w[0], bitLen, isBinary);
            Trace.Assert(w[1] == "=");
            var v = (w[2] == "NaR") ? Double.NaN : Double.Parse(w[2]);
            Console.Write($"Testing {line}");

            if ((p1.IsNaR && w[2] == "NaR") || v == p1.Value)
            {
                Console.WriteLine(" SUCCESS");
            }
            else
            {
                Console.WriteLine($" FAIL {v} != {p1}");
            }
        }
        else if (w.Length == 5)
        {

            // forms:
            // binary op binary = binary

            var p1 = Parse(w[0], bitLen, isBinary);
            var p2 = Parse(w[2], bitLen, isBinary);
            var truth = Parse(w[4], bitLen, isBinary);
            Trace.Assert(w[3] == "=");
            var (ans,fval) = w[1] switch
            {
                "+" => (p1 + p2,p1.Value+p2.Value),
                "-" => (p1 - p2,p1.Value-p2.Value),
                "*" => (p1 * p2,p1.Value*p2.Value),
                "/" => (p1 / p2, p1.Value / p2.Value),
                _ => throw new NotImplementedException($"Missing operation {w[1]}")
            };
            if (ans.Bits != truth.Bits)
            {
                ShowError(p1, p2, truth, ans, fval, w[1]);
                //Console.WriteLine($"FAIL (truth {truth}, obtained {ans}) for line {line}");
                errors++;
            }
            else
            {
                //Console.WriteLine($"SUCCESS");
                success++;
            }
        }
    }

    Console.WriteLine($"File {filename} tested, {errors} errors, {success} successes");
}

Posit Parse(string w, int bitLen, bool isBinary)
{
    var es = bitLen switch
    {
        8=>0,
        16=>1,
        32=>2,
        _=>throw new NotImplementedException()
    };
    var p = new Posit(bitLen, es);
    var val = Convert.ToInt64(w, isBinary ? 2 : 10);
    // deal with sign:
    //if (bitLen == 16 && val >= (1UL<<15))
    //    val = (ulong) ((short) v(UInt64)al);
    p.Bits = (UInt64)val;
    return p;
}

// Check roundtrip of bits
// optional filename checks all against data in file
void RoundTrip(Posit posit, string filename)
{
    Console.WriteLine($"Checking file {filename}");
    //for (var i = 0UL; i < 1UL << posit.BitLength; ++i)
    //{
    foreach (var line in File.ReadAllLines(filename))
    {
        if (line.Trim().StartsWith("#")) continue;
        var w = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Trace.Assert(w.Length == 9);
        var bitstring = w[1];
        var fval1 = w[8] == "NaR"?Double.NaN:Double.Parse(w[8]);
        var bits = Convert.ToUInt64(bitstring, 2);

        // set bits
        posit.Bits = bits;
        
        // read double out
        var fVal2 = posit.Value;

        // check value matches bit for bit
        Trace.Assert(
            BitConverter.DoubleToUInt64Bits(fVal2) == BitConverter.DoubleToUInt64Bits(fval1)
        );

        // round trip
        posit.Value = fval1;

        if (posit.Bits != bits)
        {
            Console.WriteLine($"Error posit bit mismatch {posit.Bits:X} != {bits:X}");
        }
    }
}

// end