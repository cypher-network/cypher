// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System;
using System.Numerics;
using System.Threading;
using CYPCore.Helper;

namespace CYPCore.Cryptography
{
    public struct CipherPair
    {
        public BigInteger C;
        public bool Positive;
    }

    public class Sloth
    {
        public const string Security256 =
            "60464814417085833675395020742168312237934553084050601624605007846337253615407";

        private static readonly BigInteger Zero = new(0);
        private static readonly BigInteger One = new(1);
        private static readonly BigInteger Two = new(2);
        private static readonly BigInteger Four = new(4);

        private readonly CancellationToken _stoppingToken;

        public Sloth(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="m"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public CipherPair EncodeByte(int t, byte[] m, BigInteger p)
        {
            var encryptedM = new BigInteger(m);
            for (var x = 0; x < t; x++)
            {
                encryptedM = Square(encryptedM, p);
            }

            return !IsQuadraticResidue(new BigInteger(m), p)
                ? new CipherPair { C = encryptedM, Positive = false }
                : new CipherPair { C = encryptedM, Positive = true };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="m"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public CipherPair Encode32(int t, uint m, BigInteger p)
        {
            var encryptedM = new BigInteger((long)m);
            for (var x = 0; x < t; x++)
            {
                encryptedM = Square(encryptedM, p);
            }

            return IsQuadraticResidue(new BigInteger((long)m), p)
                ? new CipherPair { C = encryptedM, Positive = true }
                : new CipherPair { C = encryptedM, Positive = false };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="cipherPair"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public BigInteger Decode(int t, CipherPair cipherPair, BigInteger p)
        {
            var c = cipherPair.C;
            var z = ModSqrtOp(t, c, p);

            return cipherPair.Positive ? z : Util.Mod(BigInteger.Negate(z), p);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="t"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public string Eval(int t, BigInteger x)
        {
            var p = BigInteger.Parse(Security256);
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
            var p = BigInteger.Parse(Security256);
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
            var t = ModExp(x, Div(Sub(p, One), Two), p);
            return t.CompareTo(One) == 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private BigInteger Mul(BigInteger x, BigInteger y)
        {
            return BigInteger.Multiply(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private BigInteger Add(BigInteger x, BigInteger y)
        {
            return BigInteger.Add(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private BigInteger Sub(BigInteger x, BigInteger y)
        {
            return BigInteger.Subtract(x, y);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private BigInteger Div(BigInteger x, BigInteger y)
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
                y = ModExp(x, Div(Add(p, One), Four), p);
            }
            else
            {
                x = Util.Mod(BigInteger.Negate(x), p);
                y = ModExp(x, Div(Add(p, One), Four), p);
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
            return ModExp(y, Two, p);
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
            var y = Zero;

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
