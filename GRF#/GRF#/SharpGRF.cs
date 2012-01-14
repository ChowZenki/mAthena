using System;
using System.IO;
using Ionic.Zlib;
using System.Collections.Generic;
using System.Text;

namespace SAIB.SharpGRF
{


    public class SharpGRF
    {

        #region local variables
        private string _filePathToGRF;
        private List<GRFFile> _GRFFiles = new List<GRFFile>();

        private int _compressedLength;
        private int _uncompressedLength;

        private byte[] _bodyBytes;

        private int _fileCount = 0;

        const int sizeOfUint = sizeof(uint);
        const int sizeOfInt = sizeof(int);
        const int sizeOfChar = sizeof(char);

        private string _signature;
        private string _encryptionKey;
        private int _fileTableOffset;
        private int _version;
        private int _m1;
        private int _m2;
        private bool _isOpen = false;

        Stream _grfStream;

        #endregion

        #region public properties
        public List<GRFFile> Files { get { return _GRFFiles; } }
        public int FileCount { get { return _fileCount; } }
        public bool IsOpen { get { return _isOpen; } }
        public int Version
        {
            get
            {
                return this._version;
            }
        }

        public int FileTableOffset
        {
            get
            {
                return this._fileTableOffset;
            }
        }

        public string Signature
        {
            get
            {
                return this._signature;
            }
        }


        public string EncryptionKey
        {
            get
            {
                return this._encryptionKey;
            }
        }

        public int M2
        {
            get
            {
                return this._m2;
            }
        }

        public int M1
        {
            get
            {
                return this._m1;
            }
        }
        #endregion


        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="SAIB.SharpGRF.SharpGRF"/> class.
        /// </summary>
        public SharpGRF() // Constructor
        {
            _signature = "Master of Magic";
            _encryptionKey = "";
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="SAIB.SharpGRF.SharpGRF"/> class.
        /// </summary>
        /// <param name='filePathToGRF'>
        /// File path to the GRF file.
        /// </param>
        public SharpGRF(string filePathToGRF) // Constructor
        {
            _filePathToGRF = filePathToGRF;

            _signature = "Master of Magic";
            _encryptionKey = "";
        }
        #endregion

        #region Public Functions

        /// <summary>
        ///  Save the GRF file.
        /// </summary>
        public void Save()
        {
            // Write to temporary file
            string tempfile = Path.GetTempFileName();
            FileStream fs = new FileStream(tempfile, FileMode.Create);
            BinaryWriter bw = new BinaryWriter(fs);

            byte[] signatureByte = new byte[Math.Max(_signature.Length, 15)];
            Encoding.ASCII.GetBytes(_signature).CopyTo(signatureByte, 0);
            bw.Write(signatureByte, 0, 15);
            bw.Write((byte)0);

            byte[] encKey = new byte[Math.Max(_encryptionKey.Length, 14)];
            Encoding.ASCII.GetBytes(_encryptionKey).CopyTo(encKey, 0);
            bw.Write(encKey, 0, 14);

            bw.Write((int)0); // will be updated later
            bw.Write((int)_m1);
            bw.Write((int)_GRFFiles.Count + _m1 + 7);
            bw.Write((int)0x200); // We always save as 2.0

            foreach (GRFFile file in _GRFFiles)
            {
                file.SaveBody(bw);
            }

            bw.Flush();

            int fileTablePos = (int)fs.Position;

            MemoryStream bodyStream = new MemoryStream();
            BinaryWriter bw2 = new BinaryWriter(bodyStream);

            foreach (GRFFile file in _GRFFiles)
            {
                file.Save(bw2);
            }

            bw2.Flush();
            byte[] compressedBody = ZlibStream.CompressBuffer(bodyStream.GetBuffer());

            bw.Write((int)compressedBody.Length);
            bw.Write((int)bodyStream.Length);
            bw.Write(compressedBody, 0, compressedBody.Length);
            bw2.Close();

            // Update file table offset
            bw.BaseStream.Seek(30, SeekOrigin.Begin);
            bw.Write((int)fileTablePos - 46);

            bw.Close();

            if (_grfStream != null)
                _grfStream.Close();

            File.Copy(tempfile, _filePathToGRF, true);

            Open();
        }

        /// <summary>
        /// Open the GRF File to start reading.
        /// </summary>
        public void Open()
        {
            string signature, encryptionKey;
            int tableOffset, version, m1, m2;
            _GRFFiles.Clear();
            _grfStream = new FileStream(_filePathToGRF, FileMode.Open);
            BinaryReader br = new BinaryReader(_grfStream);

            //Read GRF File Header -> Signature
            byte[] signatureByte = new byte[15];
            _grfStream.Read(signatureByte, 0, 15);
            signature = System.Text.Encoding.ASCII.GetString(signatureByte);

            // Read GRF File Header -> Encryption Key
            byte[] allowencryptionBytes = new byte[15];
            _grfStream.Read(allowencryptionBytes, 0, 15);
            encryptionKey = System.Text.Encoding.ASCII.GetString(allowencryptionBytes);


            tableOffset = br.ReadInt32();
            m1 = br.ReadInt32();
            m2 = br.ReadInt32();
            version = br.ReadInt32();

            this._signature = signature;
            this._encryptionKey = encryptionKey;
            this._fileTableOffset = tableOffset;
            this._m1 = m1;
            this._m2 = m2;
            this._version = version;

            _grfStream.Seek(_fileTableOffset, SeekOrigin.Current);

            _compressedLength = br.ReadInt32();
            _uncompressedLength = br.ReadInt32();

            byte[] compressedBodyBytes = new byte[_compressedLength];
            _grfStream.Read(compressedBodyBytes, 0, _compressedLength);

            _bodyBytes = ZlibStream.UncompressBuffer(compressedBodyBytes);
            _fileCount = m2 - m1 - 7;

            MemoryStream bodyStream = new MemoryStream(_bodyBytes);
            BinaryReader bodyReader = new BinaryReader(bodyStream);

            for (int x = 0; x < _fileCount; x++)
            {
                string fileName = string.Empty;
                char currentChar;

                while ((currentChar = (char)bodyReader.ReadByte()) != 0)
                    fileName += currentChar;

                int fileCompressedLength = 0,
                    fileCompressedLengthAligned = 0,
                    fileUncompressedLength = 0,
                    fileOffset = 0,
                    fileCycle = 0;

                char fileFlags = (char)0;

                fileCompressedLength = bodyReader.ReadInt32();
                fileCompressedLengthAligned = bodyReader.ReadInt32();
                fileUncompressedLength = bodyReader.ReadInt32();
                fileFlags = (char)bodyReader.ReadByte();
                fileOffset = bodyReader.ReadInt32();

                if (fileFlags == 3)
                {
                    int lop, srccount, srclen = fileCompressedLength;

                    for (lop = 10, srccount = 1; srclen >= lop; lop *= 10, srccount++)
                        fileCycle = srccount;
                }

                GRFFile newGRFFile = new GRFFile(
                    System.Text.Encoding.GetEncoding("EUC-KR").GetString(System.Text.Encoding.Default.GetBytes(fileName)),
                    fileCompressedLength,
                    fileCompressedLengthAligned,
                    fileUncompressedLength,
                    fileFlags,
                    fileOffset,
                    fileCycle,
                    this);

                _GRFFiles.Add(newGRFFile);

            }
            _isOpen = true;
        }

        /// <summary>
        ///  Open the GRF File to start reading. (Overload)
        /// </summary>
        /// <param name='filePath'>
        /// Path the the grf file to be opened
        /// </param>
        public void Open(string filePath)
        {
            _filePathToGRF = filePath;
            Open();
        }

        /// <summary>
        /// Closes the grf so it can be used again
        /// </summary>
        public void Close()
        {
            _grfStream.Close();
            _isOpen = false;
        }

        /// <summary>
        /// Gets the data of the file in the grf.
        /// </summary>
        /// <returns>
        /// byte[] the data in bytes
        /// </returns>
        /// <param name='file'>
        /// (GRFFile) The file to get
        /// </param>
        public byte[] GetDataFromFile(GRFFile file)
        {
            byte[] compressedBody = new byte[file.CompressedLength];

            _grfStream.Seek(46 + file.Offset, SeekOrigin.Begin);
            _grfStream.Read(compressedBody, 0, file.CompressedLengthAligned);

            if ((file.Flags == 3) || (file.Flags == 5))
            {
                compressedBody = DES.Decode(compressedBody, file.CompressedLengthAligned, file.Cycle);
            }

            return ZlibStream.UncompressBuffer(compressedBody);
        }

        public byte[] GetOriginalDataFromFile(GRFFile file)
        {
            byte[] compressedBody = new byte[file.CompressedLength];

            _grfStream.Seek(46 + file.Offset, SeekOrigin.Begin);
            _grfStream.Read(compressedBody, 0, file.CompressedLengthAligned);

            return compressedBody;
        }
        #endregion

        public void AddNewFile(string name, byte[] data)
        {
            int i = 0;

            foreach (GRFFile file in _GRFFiles)
            {
                if (file.Name.ToLower() == name.ToLower())
                {
                    _GRFFiles[i].UncompressedBody = data;

                    return;
                }

                i++;
            }

            GRFFile f = new GRFFile(name, 0, 0, 0, (char)0, 0, 0, this);
            f.UncompressedBody = data;
            _GRFFiles.Add(f);
        }
    }
}

