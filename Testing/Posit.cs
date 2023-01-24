/*
MIT License

Copyright (c) 2023 Chris Lomont

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE. 
 */

using System.Diagnostics;
using System.Numerics;
using static System.Math;

namespace Lomont;

/// <summary>
/// Represent a generic posit
/// A posit is a floating-point format
/// </summary>
public class Posit 
    : IAdditionOperators<Posit, Posit, Posit>
        , ISubtractionOperators<Posit, Posit, Posit>
        , IMultiplyOperators<Posit, Posit, Posit>
        , IDivisionOperators<Posit, Posit, Posit>
        , IComparisonOperators<Posit,Posit,bool>
        , IEqualityOperators<Posit,Posit,bool>
        , IUnaryNegationOperators<Posit,Posit>
        , IUnaryPlusOperators<Posit,Posit>
    //, IExponentialFunctions<Posit,Posit>
    //, ILogarithmicFunctions<Posit,Posit>
    //, IPowerFunctions<Posit,Posit>
    //, ITrigonometricFunctions<Posit,Posit>
    //, IFloatingPoint<Posit>
{
    // based on posit standard March 2,2022 https://posithub.org/docs/posit_standard-2.pdf

    /*
     \Format:
    n = total bit length, often 8,16,32
    es = # of exponent bits, 0,1,2 for standard 8,16,32 lengths
    
    sign bit first, 1 = negative, 0 = positive
    then k bits of same sign, then (if space left) bit of opposite sign to terminate k.
    The k bits and (if space left) opposite bit are called the 'regime'
    if regime bits are 0, then treat as negative -k, else treat as non-neg value (k-1)
    then (if space), es bits for exponent (as nonn-egative value)
    rest of bits f are fraction bits with implied 1 (except 0?)

    let useed = 2^(2^es)

    then val is (-1)^s * useed^k 2^e (1+f)

    Fact: posit32,16,8 fit perfectly in a float64, so can use float64 ro do math, then round and convert

    ex: 4 bits at most, k
    0000 -4
    0001 -3
    001x -2
    01xx -1
    10xx  0
    110x  1
    1110  2
    1111  3
     
     */

    public static Posit Posit8(double value = 0) => new Posit(8, 0, value);
    public static Posit Posit16(double value = 0) => new Posit(16, 1, value);
    public static Posit Posit32(double value = 0) => new Posit(32, 2, value);


    public Posit(Posit val)
    {
        this.n = val.n;
        this.es = val.es;
        this.Value = val.Value;
    }
    public Posit(int n, int es, double value = 0)
    {
        Trace.Assert(n >= 5); // todo - bound on es? bits? what is rule?
        Trace.Assert(es >= 0);
        this.n = n;
        this.es = es;
        Value = value;
    }

    public double Value
    {
        get => GetVal();
        set => Round(value);
    }

    /// <summary>
    /// Set as bits, sign extended, excess masked out
    /// </summary>
    public ulong Bits
    {
        get => bits;
        set => bits = (value) & ((1UL << n) - 1);
    }

    /// <summary>
    /// Total number of bits
    /// </summary>
    public int BitLength => n;

    public bool IsNaR => bits == (1UL << (n - 1));

    public string Bitstring {
        get
        {
            var bs = Convert.ToString((long)bits, 2);
            if (bs.Length < n)
                bs = new string('0', n - bs.Length) + bs;
            return bs;
        }
    }

    public override string ToString() => "P'"+ GetVal();

    #region Implementation

    // round long representation to desired length
    ulong RoundBits(int bitLen, ulong positBits64)
    {
        // rules from standard 2022
        // u,w are n bit posits st u < x < w and interval (u,v) has no other posits
        // let U be the n-bit representation of u
        // let v = n+1 bit posit with rep U1  (U append 1)
        // if u < x < v or (x==v && LSB(u) == 0) then
        //   return u
        // else
        //   return w

        var dBits = 64 - bitLen;
        if (positBits64 == (1UL << 63) >> bitLen)
            return (positBits64 * 2)>>dBits; // special case: if value is 1/2 ulp, rounds up, not down ?!

        // bankers rounding
        var halfUlp = ((1UL << 63) - 1) >> bitLen; // ...0007ffff... just less than ulp/2
        halfUlp += (positBits64 >> dBits) & 1; // odd is 008000.. (round up for tie)
        //var t1 = positBits64 >> dBits;
        positBits64 += halfUlp; // add half up
        //var t2 = positBits64 >> dBits;
        //if (t1 != t2) Console.Write($" - round up - ");
        return positBits64 >> dBits;
    }

    /// <summary>
    /// Round val into bits field
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    void Round(double value)
    {
        // spec: 
        // if x representable, return posit of x
        // if |x| > maxposit return maxposit*sign(x)
        // if |x| < minposit return minposit*sign(x)
        // u,w are n bit posits st u < x < w and interval (u,v) has no other posits
        // let U be the n-bit representation of u
        // let v = n+1 bit posit with rep U1  (U append 1)
        // if u < x < v or (x==v && LSB(u) == 0) then
        //   return u
        // else
        //   return w

        if (value == 0)
        {
            bits = 0;
            return;
        }

        if (!double.IsFinite(value))
        {
            bits = 1UL << (n - 1); // NaR representation
            return;
        }

        var me = (1 << es) * (n - 2);
        var minpos = Math.Pow(2, -me); // exact?
        var maxpos = 1 / minpos; // exact
        if (Math.Abs(value) >= maxpos)
        {
            bits= (1UL<<(n-1))-1;
            if (value < 0) bits = ~bits+1;
            bits &= (1UL << n) - 1;
            return;
        }
        if (Math.Abs(value) < minpos)
        {
            bits = 1;
            if (value < 0) bits = ~bits + 1;
            bits &= (1UL << n) - 1;
            return;
        }

        // IEEE 754 float64 format:
        // 1 sign bit, 11 exponent bits, 52 fraction bits
        var b64 = BitConverter.DoubleToUInt64Bits(value);
        var e64 = (b64 >> 52) & ((1UL << 11) - 1); // exponent bits
        var f64 = b64 & ((1UL << 52) - 1); // fraction bits

        var e = ((int)e64) - 1023; // unbias exponent
        var expSign = e < 0 ? 1 : 0;
        var k = e >> es;           // for useed^k calc

        // create: 2 last regime bits, exponent bits, mantissa bits, at top of 64 bit value
        var regBits = (1UL << 63) >> expSign; // 01000... for |x| < 1 else 1000....
        var expBits = (ulong)(e & ((1 << es) - 1));
        expBits <<= 64 - 2 - es; // align
        var fracBits = f64 << (11 - es - 1); // # bits for float64 exponent

        var bits64 = regBits | expBits | fracBits;
        bits64 = (ulong)(((long)bits64) >> (Abs(k + 1) + expSign)); // treat as signed to extend bits
        bits64 &= ~(1UL << 63); // remove possible leftover sign

        // round to nearest n-bit posit
        var rounded = RoundBits(n, bits64);

        // no underflow or overflow
        var kmax = (1023 >> es) + 1; // check unbounded
        if (n <= Abs(k) && Abs(k) < kmax)
            rounded = (ulong) ((long) rounded - Sign(k));

        if (value < 0)
            rounded = (ulong)(-(long)rounded); // two's complement for neg
        bits = rounded & ((1UL << n) - 1);
    }

    // bits
    ulong bits = 0;

    // sizes of fields
    int n, es;


    static uint Bit(ulong v, int index)
    {
        var mask = 1UL << index;
        return (v & mask) == 0 ? 0U : 1U;
    }

    double GetVal()
    {
        // convert bits to a double
        var lBits = bits;

        if (lBits == 0) return 0.0; // simply remove this case here
        if (IsNaR) return Double.NaN; // replace with this


        // sign
        //var s = 3 * Bit(bits, n - 1) - 1; // +- 1
        int s = (int)Bit(lBits, n - 1); // this must be treated as a signed integer

        // regime: k identical bits
        int i = n - 2; // bit pos, walk bits in value
        var b = Bit(lBits, i); // bit val
        var k = 0;
        while (i >= 0 && Bit(lBits, i) == b)
        {
            k++;
            i--;
        }

        // regime value r
        var r = b == 0 ? -k : k - 1;
        // i points to last of regime, move down one
        i--;

        // exponent
        var e = 0L;
        for (var z = 0; z < es; ++z)
        {
            // get valid bits if any, else 0
            var bit = i >= 0 ? Bit(lBits, i) : 0;
            e = 2 * e + bit;
            i--;
        }

        // fraction is max(0,n-3-es) bits, any past end are 0
        var frac = 0UL;
        var m = Max(0, n - 3 - es); // # frac bits
        for (var z = 0; z < m; ++z)
        {
            // get valid bits if any, else 0
            var bit = i >= 0 ? Bit(lBits, i) : 0;
            frac = 2 * frac + bit;
            i--;
        }

        // frac is divided by 2^m, so is 0 <= f < 1

        // for es = 2
        // posit p = (1-3s+f) * 2^((1-2s)*(4r+e+s))

        // now convert:
        double df = frac; // exact
        df /= 1 << m; // exact
        df = (1 - 3 * s + df); // exact?
        var bb = 1 << es; // 4 if es = 2
        var dexp = (1 - 2 * s) * (bb * r + e + s);
        df *= Pow(2, dexp); // exact?
        return df;
    }

    /// <summary>
    /// Throw is not same type
    /// </summary>
    static void Check(Posit a, Posit b)
    {
        Trace.Assert(a.n == b.n);
        Trace.Assert(a.es == b.es);
    }

    #endregion

    #region Math

    public static Posit Sqrt(Posit value)
    {
        if (value.Value < 0)
            return new Posit(value.n, value.es, Double.NaN);
        return new Posit(value.n, value.es, Math.Sqrt(value.Value));
    }


    public static Posit operator +(Posit left, Posit right)
    {
        Check(left, right);
        return new Posit(left.n, left.es, left.Value + right.Value);
    }

    public static Posit operator -(Posit left, Posit right)
    {
        Check(left, right);
        return new Posit(left.n, left.es, left.Value - right.Value);
    }

    public static Posit operator *(Posit left, Posit right)
    {
        Check(left, right);
        return new Posit(left.n, left.es, left.Value * right.Value);
    }

    public static Posit operator /(Posit left, Posit right)
    {
        Check(left, right);
        return new Posit(left.n, left.es, left.Value / right.Value);
    }

    public static bool operator >(Posit left, Posit right)
    {
        Check(left, right);
        return left.Value > right.Value;
    }

    public static bool operator >=(Posit left, Posit right)
    {
        Check(left, right);
        return left.Value >= right.Value;
    }

    public static bool operator <(Posit left, Posit right)
    {
        Check(left, right);
        return left.Value < right.Value;
    }

    public static bool operator <=(Posit left, Posit right)
    {
        Check(left, right);
        return left.Value <= right.Value;
    }

    public static bool operator ==(Posit? left, Posit? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }
    public override bool Equals(object? obj)
    {
        var other = obj as Posit;
        if (other != null)
        {
            Check(this, other);
            return this.bits == other.bits;
        }
        else 
            return false;
    }

    public static bool operator !=(Posit? left, Posit? right)
    {
        return !(left == right);
    }

    public static Posit operator -(Posit value)
    {
        return new Posit(value.n, value.es, -value.Value);
    }

    public static Posit operator +(Posit value)
    {
        return value; 
    }
    #endregion


    /// <summary>
    /// Apply a unary function to the posit, return new posit
    /// </summary>
    /// <param name="func"></param>
    public Posit Unary(Func<double, double> func)
    {
        var p = new Posit(this)
        {
            Value = func(Value)
        };
        return p;
    }
    public static Posit Binary(Posit lhs, Posit rhs, Func<double, double, double> func)
    {
        Check(lhs, rhs); // same format!
        var p = new Posit(lhs)
        {
            Value = func(lhs.Value, rhs.Value)
        };
        return p;
    }

}

/// <summary>
/// Posit math functions
/// </summary>
public static class MathP
{
    public static Posit Cos(Posit angle) => angle.Unary(Math.Cos);
    public static Posit Sin(Posit angle) => angle.Unary(Math.Sin);
    public static Posit Tan(Posit angle) => angle.Unary(Math.Tan);
    public static Posit Acos(Posit value) => value.Unary(Math.Acos);
    public static Posit Asin(Posit value) => value.Unary(Math.Asin);
    public static Posit Atan(Posit value) => value.Unary(Math.Atan);
    public static Posit Sqrt(Posit value) => value.Unary(Math.Sqrt);
    public static Posit Pow(Posit baseValue, Posit power) => Posit.Binary(baseValue, power, Math.Pow);
    public static Posit Exp(Posit value) => value.Unary(Math.Exp);
    public static Posit Log(Posit a, Posit newBase) => Posit.Binary(a, newBase, Math.Log);
    public static Posit Log(Posit a) => a.Unary(Math.Log);
    public static Posit Log10(Posit value) => value.Unary(Math.Log10);
    public static Posit Log2(Posit angle) => angle.Unary(Math.Log2);
}