# Posit library

Chris Lomont 2023

This is a single file, MIT licensed, C# implementation of the floating point type **posit**. I needed a good C# implementation for some numerical experiments, and could find none.

See https://en.wikipedia.org/wiki/Unum_(number_format)#Unum_III

This implements generic posits of the form `posit(n,es)` where n is bit length and es is the exponent size. Standard posits sizes are posit8 = Posit(8,0), Posit16 = Posit(16,1), and Posit32 = Posit(32,2).

It implements standard operations `+,-,*,/`, and `sqrt`, trig operations `Cos,Sin,Tan,Acos,Asin,Atan`, and the remaining exponential operations `Pow,Exp,Log,Log10,Log2`.

It currently passes all my tests (posit 8 checked for all values, others tested for millions of randomized expressions) to match the default library https://posithub.org/khub_community#softposit. While developing this lib, I found many other libs that disagree with the standard implementation. 

The Papers directory holds many papers relevant to posit and related number systems.

## History

v0.5 - Jan 24, 2023 - initial release
