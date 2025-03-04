﻿using System;
using System.IO;
using System.Collections.Generic;

namespace REE.Unpacker
{
    class PakUnpack
    {
        private static Int32 dwEntrySize = 0;
        private static List<PakEntry> m_EntryTable = new List<PakEntry>();

        public static void iDoIt(String m_PakFile, String m_DstFolder)
        {
            using (FileStream TPakStream = File.OpenRead(m_PakFile))
            {
                if (TPakStream.Length <= 16)
                {
                    Utils.iSetError("[ERROR]: Empty PAK archive file");
                    return;
                }

                var m_Header = new PakHeader();

                m_Header.dwMagic = TPakStream.ReadUInt32();
                m_Header.bMajorVersion = TPakStream.ReadByte();
                m_Header.bMinorVersion = TPakStream.ReadByte();
                m_Header.wFeature = TPakStream.ReadInt16();
                m_Header.dwTotalFiles = TPakStream.ReadInt32();
                m_Header.dwHash = TPakStream.ReadUInt32();

                if (m_Header.dwMagic != 0x414B504B)
                {
                    Utils.iSetError("[ERROR]: Invalid magic of PAK archive file");
                    return;
                }

                if (m_Header.bMajorVersion != 2 && m_Header.bMajorVersion != 4 || m_Header.bMinorVersion != 0)
                {
                    Utils.iSetError("[ERROR]: Invalid version of PAK archive file -> " + m_Header.bMajorVersion.ToString() + "." + m_Header.bMinorVersion.ToString() + ", expected 2.0 & 4.0");
                    return;
                }

                switch (m_Header.bMajorVersion)
                {
                    case 2: dwEntrySize = 24; break;
                    case 4: dwEntrySize = 48; break;
                    default: break;
                }

                var lpTable = TPakStream.ReadBytes(m_Header.dwTotalFiles * dwEntrySize);

                if (m_Header.wFeature == 8)
                {
                    var lpEncryptedKey = TPakStream.ReadBytes(128);

                    lpTable = PakCipher.iDecryptData(lpTable, lpEncryptedKey);
                }

                m_EntryTable.Clear();
                using (var TEntryReader = new MemoryStream(lpTable))
                {
                    for (Int32 i = 0; i < m_Header.dwTotalFiles; i++)
                    {
                        var m_Entry = new PakEntry();

                        if (m_Header.bMajorVersion == 2)
                        {
                            m_Entry.dwOffset = TEntryReader.ReadInt64();
                            m_Entry.dwDecompressedSize = TEntryReader.ReadInt64();
                            m_Entry.dwHashNameLower = TEntryReader.ReadUInt32();
                            m_Entry.dwHashNameUpper = TEntryReader.ReadUInt32();
                            m_Entry.dwCompressedSize = 0;
                            m_Entry.wCompressionType = 0;
                            m_Entry.dwChecksum = 0;
                        }
                        else
                        {
                            m_Entry.dwHashNameLower = TEntryReader.ReadUInt32();
                            m_Entry.dwHashNameUpper = TEntryReader.ReadUInt32();
                            m_Entry.dwOffset = TEntryReader.ReadInt64();
                            m_Entry.dwCompressedSize = TEntryReader.ReadInt64();
                            m_Entry.dwDecompressedSize = TEntryReader.ReadInt64();
                            m_Entry.wCompressionType = PakUtils.iGetCompressionType(TEntryReader.ReadInt64());
                            m_Entry.dwChecksum = TEntryReader.ReadUInt64();
                        }
                        m_EntryTable.Add(m_Entry);
                    }
                }
				
                Int32 dwCounter = 1;
                foreach (var m_Entry in m_EntryTable)
                {
                    String m_FileName = PakList.iGetNameFromHashList((UInt64)m_Entry.dwHashNameUpper << 32 | m_Entry.dwHashNameLower);
                    String m_FullPath = m_DstFolder + m_FileName.Replace("/", @"\");
					
                    Console.Title = Program.m_Title + " - " + Path.GetFileName(m_PakFile) + " -> " + PakUtils.iPrintInfo(dwCounter++, (Int32)m_Header.dwTotalFiles);

                    Utils.iSetInfo("[UNPACKING]: " + m_FileName);
                    Utils.iCreateDirectory(m_FullPath);

                    TPakStream.Seek(m_Entry.dwOffset, SeekOrigin.Begin);
                    if (m_Entry.wCompressionType == PakFlags.NONE)
                    {
                        var lpBuffer = TPakStream.ReadBytes(PakUtils.iGetSize(m_Entry.dwCompressedSize, m_Entry.dwDecompressedSize));
                        m_FullPath = PakUtils.iDetectFileType(m_FullPath, lpBuffer);
                        
                        File.WriteAllBytes(m_FullPath, lpBuffer);
                    }
                    else if (m_Entry.wCompressionType == PakFlags.DEFLATE || m_Entry.wCompressionType == PakFlags.ZSTD)
                    {
                        var lpSrcBuffer = TPakStream.ReadBytes((Int32)m_Entry.dwCompressedSize);
                        var lpDstBuffer = new Byte[] { };

                        switch (m_Entry.wCompressionType)
                        {
                            case PakFlags.DEFLATE: lpDstBuffer = DEFLATE.iDecompress(lpSrcBuffer); break;
                            case PakFlags.ZSTD: lpDstBuffer = ZSTD.iDecompress(lpSrcBuffer); break;
                        }

                        m_FullPath = PakUtils.iDetectFileType(m_FullPath, lpDstBuffer);

                        File.WriteAllBytes(m_FullPath, lpDstBuffer);
                    }
                    else
                    {
                        Utils.iSetError("[ERROR]: Unknown compression id detected -> " + m_Entry.wCompressionType.ToString());
                        return;
                    }
                }
				
                Console.Title = Program.m_Title;

                TPakStream.Dispose();
            }
        }
    }
}
