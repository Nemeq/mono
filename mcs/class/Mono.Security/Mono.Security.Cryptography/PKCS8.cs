//
// PKCS8.cs: PKCS #8 - Private-Key Information Syntax Standard
//	ftp://ftp.rsasecurity.com/pub/pkcs/doc/pkcs-8.doc
//
// Author:
//	Sebastien Pouliot (spouliot@motus.com)
//
// (C) 2003 Motus Technologies Inc. (http://www.motus.com)
//

using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;

using Mono.Security.Cryptography;
using Mono.Security.X509;

namespace Mono.Security.Cryptography {

	public class PKCS8 {

		public enum KeyInfo {
			PrivateKey,
			EncryptedPrivateKey,
			Unknown
		}

		static public KeyInfo GetType (byte[] data) 
		{
			if (data == null)
				throw new ArgumentNullException ("data");

			KeyInfo ki = KeyInfo.Unknown;
			try {
				ASN1 top = new ASN1 (data);
				if ((top.Tag == 0x30) && (top.Count > 0)) {
					ASN1 firstLevel = top [0];
					switch (firstLevel.Tag) {
						case 0x02:
							ki = KeyInfo.PrivateKey;
							break;
						case 0x30:
							ki = KeyInfo.EncryptedPrivateKey;
							break;
					}
				}
			}
			catch {
				throw new CryptographicException ("invalid ASN.1 data");
			}
			return ki;
		}

		/*
		 * PrivateKeyInfo ::= SEQUENCE {
		 *	version Version,
		 *	privateKeyAlgorithm PrivateKeyAlgorithmIdentifier,
		 *	privateKey PrivateKey,
		 *	attributes [0] IMPLICIT Attributes OPTIONAL 
		 * }
		 * 
		 * Version ::= INTEGER
		 * 
		 * PrivateKeyAlgorithmIdentifier ::= AlgorithmIdentifier
		 * 
		 * PrivateKey ::= OCTET STRING
		 * 
		 * Attributes ::= SET OF Attribute
		 */
		public class PrivateKeyInfo {

			private int _version;
			private string _algorithm;
			private byte[] _key;
			private ArrayList _list;

			public PrivateKeyInfo () 
			{
				_version = 0;
				_list = new ArrayList ();
			}

			public PrivateKeyInfo (byte[] data) : this () 
			{
				Decode (data);
			}

			// properties

			public string Algorithm {
				get { return _algorithm; }
				set { _algorithm = value; }
			}

			public ArrayList Attributes {
				get { return _list; }
			}

			public byte[] PrivateKey {
				get { return _key; }
				set { _key = value; }
			}

			public int Version {
				get { return _version; }
				set { 
					if (_version < 0)
						throw new ArgumentOutOfRangeException ("negative version");
					_version = value; 
				}
			}

			// methods

			private void Decode (byte[] data) 
			{
				ASN1 privateKeyInfo = new ASN1 (data);
				if (privateKeyInfo.Tag != 0x30)
					throw new CryptographicException ("invalid PrivateKeyInfo");

				ASN1 version = privateKeyInfo [0];
				if (version.Tag != 0x02)
					throw new CryptographicException ("invalid version");
				_version = version.Value [0];

				ASN1 privateKeyAlgorithm = privateKeyInfo [1];
				if (privateKeyAlgorithm.Tag != 0x30)
					throw new CryptographicException ("invalid algorithm");
				
				ASN1 algorithm = privateKeyAlgorithm [0];
				if (algorithm.Tag != 0x06)
					throw new CryptographicException ("missing algorithm OID");
				_algorithm = ASN1Convert.ToOID (algorithm);

				ASN1 privateKey = privateKeyInfo [2];
				_key = privateKey.Value;

				// attributes [0] IMPLICIT Attributes OPTIONAL
				if (privateKeyInfo.Count > 3) {
					ASN1 attributes = privateKeyInfo [3];
					for (int i=0; i < attributes.Count; i++) {
						_list.Add (attributes [i]);
					}
				}
			}

			// TODO
			public byte[] GetBytes () 
			{
				return null;
			}

			// static methods

			static private byte[] RemoveLeadingZero (byte[] bigInt) 
			{
				int start = 0;
				int length = bigInt.Length;
				if (bigInt [0] == 0x00) {
					start = 1;
					length--;
				}
				byte[] bi = new byte [length];
				Buffer.BlockCopy (bigInt, start, bi, 0, length);
				return bi;
			}

			static private byte[] Normalize (byte[] bigInt, int length) 
			{
				if (bigInt.Length == length)
					return bigInt;
				else if (bigInt.Length > length)
					return RemoveLeadingZero (bigInt);
				else {
					// pad with 0
					byte[] bi = new byte [length];
					Buffer.BlockCopy (bigInt, 0, bi, (length - bigInt.Length), bigInt.Length);
					return bi;
				}
			}
			
			/*
			 * RSAPrivateKey ::= SEQUENCE {
			 *	version           Version, 
			 *	modulus           INTEGER,  -- n
			 *	publicExponent    INTEGER,  -- e
			 *	privateExponent   INTEGER,  -- d
			 *	prime1            INTEGER,  -- p
			 *	prime2            INTEGER,  -- q
			 *	exponent1         INTEGER,  -- d mod (p-1)
			 *	exponent2         INTEGER,  -- d mod (q-1) 
			 *	coefficient       INTEGER,  -- (inverse of q) mod p
			 *	otherPrimeInfos   OtherPrimeInfos OPTIONAL 
			 * }
			 */
			static public RSA DecodeRSA (byte[] encryptedKeypair) 
			{
				ASN1 privateKey = new ASN1 (encryptedKeypair);
				if (privateKey.Tag != 0x30)
					throw new CryptographicException ("invalid private key format");

				ASN1 version = privateKey [0];
				if (version.Tag != 0x02)
					throw new CryptographicException ("missing version");

				if (privateKey.Count < 9)
					throw new CryptographicException ("not enough key parameters");

				RSAParameters param = new RSAParameters ();
				// note: MUST remove leading 0 - else MS wont import the key
				param.Modulus = RemoveLeadingZero (privateKey [1].Value);
				int keysize = param.Modulus.Length;
				int keysize2 = (keysize >> 1); // half-size
				// size must be normalized - else MS wont import the key
				param.D = Normalize (privateKey [3].Value, keysize);
				param.DP = Normalize (privateKey [6].Value, keysize2);
				param.DQ = Normalize (privateKey [7].Value, keysize2);
				param.Exponent = RemoveLeadingZero (privateKey [2].Value);
				param.InverseQ = Normalize (privateKey [8].Value, keysize2);
				param.P = Normalize (privateKey [4].Value, keysize2);
				param.Q = Normalize (privateKey [5].Value, keysize2);

				RSA rsa = RSA.Create ();
				rsa.ImportParameters (param);
				return rsa;
			}

			// DSA only encode it's X private key inside an ASN.1 INTEGER (Hint: Tag == 0x02)
			// which isn't enough for rebuilding the keypair. The other parameters
			// can be found (98% of the time) in the X.509 certificate associated
			// with the private key or (2% of the time) the parameters are in it's
			// issuer X.509 certificate (not supported in the .NET framework).
			static public DSA DecodeDSA (byte[] encryptedPrivateKey, DSAParameters dsaParameters) 
			{
				ASN1 privateKey = new ASN1 (encryptedPrivateKey);
				if (privateKey.Tag != 0x02)
					throw new CryptographicException ("invalid private key format");

				// X is ALWAYS 20 bytes (no matter if the key length is 512 or 1024 bits)
				dsaParameters.X = Normalize (encryptedPrivateKey, 20);
				DSA dsa = DSA.Create ();
				dsa.ImportParameters (dsaParameters);
				return dsa;
			}
		}

		/*
		 * EncryptedPrivateKeyInfo ::= SEQUENCE {
		 *	encryptionAlgorithm EncryptionAlgorithmIdentifier,
		 *	encryptedData EncryptedData 
		 * }
		 * 
		 * EncryptionAlgorithmIdentifier ::= AlgorithmIdentifier
		 * 
		 * EncryptedData ::= OCTET STRING
		 * 
		 * --
		 *  AlgorithmIdentifier  ::= SEQUENCE {
		 *	algorithm  OBJECT IDENTIFIER,
		 *	parameters ANY DEFINED BY algorithm OPTIONAL
		 * }
		 * 
		 * -- from PKCS#5
		 * PBEParameter ::= SEQUENCE {
		 *	salt OCTET STRING SIZE(8),
		 *	iterationCount INTEGER 
		 * }
		 */
		public class EncryptedPrivateKeyInfo {

			private string _algorithm;
			private byte[] _salt;
			private int _iterations;
			private byte[] _data;

			public EncryptedPrivateKeyInfo () {}

			public EncryptedPrivateKeyInfo (byte[] data) : this () 
			{
				Decode (data);
			}

			// properties

			public string Algorithm {
				get { return _algorithm; }
			}

			public byte[] EncryptedData {
				get { return (byte[]) _data.Clone (); }
			}

			public byte[] Salt {
				get { return (byte[]) _salt.Clone (); }
			}

			public int IterationCount {
				get { return _iterations; }
			}

			// methods

			private void Decode (byte[] data) 
			{
				ASN1 encryptedPrivateKeyInfo = new ASN1 (data);
				if (encryptedPrivateKeyInfo.Tag != 0x30)
					throw new CryptographicException ("invalid EncryptedPrivateKeyInfo");

				ASN1 encryptionAlgorithm = encryptedPrivateKeyInfo [0];
				if (encryptionAlgorithm.Tag != 0x30)
					throw new CryptographicException ("invalid encryptionAlgorithm");
				ASN1 algorithm = encryptionAlgorithm [0];
				if (algorithm.Tag != 0x06)
					throw new CryptographicException ("invalid algorithm");
				_algorithm = ASN1Convert.ToOID (algorithm);
				// parameters ANY DEFINED BY algorithm OPTIONAL
				if (encryptionAlgorithm.Count > 1) {
					ASN1 parameters = encryptionAlgorithm [1];
					if (parameters.Tag != 0x30)
						throw new CryptographicException ("invalid parameters");

					ASN1 salt = parameters [0];
					if (salt.Tag != 0x04)
						throw new CryptographicException ("invalid salt");
					_salt = salt.Value;

					ASN1 iterationCount = parameters [1];
					if (iterationCount.Tag != 0x02)
						throw new CryptographicException ("invalid iterationCount");
					_iterations = ASN1Convert.ToInt32 (iterationCount);
				}

				ASN1 encryptedData = encryptedPrivateKeyInfo [1];
				if (encryptedData.Tag != 0x04)
					throw new CryptographicException ("invalid EncryptedData");
				_data = encryptedData.Value;
			}

			// Note: PKCS#8 doesn't define how to generate the key required for encryption
			// so you're on your own. Just don't try to copy the big guys too much ;)
			// Netscape:	http://www.cs.auckland.ac.nz/~pgut001/pubs/netscape.txt
			// Microsoft:	http://www.cs.auckland.ac.nz/~pgut001/pubs/breakms.txt
			public byte[] GetBytes (byte[] encryptedPrivateKey)
			{
				// TODO
				return null;
			}
		}
	}
}
