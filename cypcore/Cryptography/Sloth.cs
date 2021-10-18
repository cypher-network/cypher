// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Numerics;
using System.Threading;
using CYPCore.Helper;

namespace CYPCore.Cryptography
{
    /// <summary>
    /// 
    /// </summary>
    public class Sloth
    {
        private const string PrimeBit256 =
            "60464814417085833675395020742168312237934553084050601624605007846337253615407";

        private readonly CancellationToken _stoppingToken;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        public Sloth(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public string Eval(int t, BigInteger x)
        {
            var p = BigInteger.Parse(PrimeBit256);
            var y = ModSqrtOp(t, x, p);

            return y.ToString();
        }

        /// <summary>
        ///     
        /// </summary>
        /// <param name="t"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Verify(uint t, BigInteger x, BigInteger y)
        {
            var p = BigInteger.Parse(PrimeBit256);
            if (!IsQuadraticResidue(x, p))
            {
                x = Util.Mod(BigInteger.Negate(x), p);
            }
            for (var i = 0; i < t; i++)
            {
                y = Square(y, p);
            }

            return x.CompareTo(y) == 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="exponent"></param>
        /// <param name="modulus"></param>
        /// <returns></returns>
        private BigInteger ModExp(BigInteger value, BigInteger exponent, BigInteger modulus)
        {
            return BigInteger.ModPow(value, exponent, modulus);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        private bool IsQuadraticResidue(BigInteger x, BigInteger p)
        {
            var t = ModExp(x, Div(Sub(p, new BigInteger(1)), new BigInteger(2)), p);
            return t.CompareTo(new BigInteger(1)) == 0;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static BigInteger Add(BigInteger x, BigInteger y)
        {
            return BigInteger.Add(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static BigInteger Sub(BigInteger x, BigInteger y)
        {
            return BigInteger.Subtract(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static BigInteger Div(BigInteger x, BigInteger y)
        {
            return BigInteger.Divide(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        private BigInteger ModSqrt(BigInteger x, BigInteger p)
        {
            BigInteger y;
            if (IsQuadraticResidue(x, p))
            {
                y = ModExp(x, Div(Add(p, new BigInteger(1)), new BigInteger(4)), p);
            }
            else
            {
                x = Util.Mod(BigInteger.Negate(x), p);
                y = ModExp(x, Div(Add(p, new BigInteger(1)), new BigInteger(4)), p);
            }

            return y;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="y"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        private BigInteger Square(BigInteger y, BigInteger p)
        {
            return ModExp(y, new BigInteger(2), p);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="x"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        private BigInteger ModSqrtOp(int t, BigInteger x, BigInteger p)
        {
            var y = new BigInteger(0);

            try
            {
                y = x;
                for (var i = 0; i < t; i++)
                {
                    _stoppingToken.ThrowIfCancellationRequested();
                    y = ModSqrt(y, p);
                }

                return y;
            }
            catch (OperationCanceledException)
            {
            }

            return y;
        }
    }
}
