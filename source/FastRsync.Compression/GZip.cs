// this implementation is influenced by pigz tool by Mark Adler: https://github.com/madler/pigz/blob/master/pigz.c
// pigz license:
/*
  This software is provided 'as-is', without any express or implied
  warranty. In no event will the author be held liable for any damages
  arising from the use of this software.
  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:
  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.
  Mark Adler
  madler@alumni.caltech.edu
 */

using System;
using System.IO;
using System.Threading.Tasks;
using Ionic.Zlib;

namespace FastRsync.Compression
{
    public class GZip
    {
        const int RSYNCBITS = 12;
        const uint RSYNCMASK = ((1U << RSYNCBITS) - (uint)1);
        const uint RSYNCHIT = (RSYNCMASK >> 1);

        const int BUFFER_SIZE = 32 * 1024;

        public static void Compress(Stream sourceStream, Stream destStream)
        {
            var buffer = new byte[BUFFER_SIZE];

            using (var compressor = new GZipStream(destStream, Ionic.Zlib.CompressionMode.Compress,
                CompressionLevel.BestSpeed, true))
            {
                compressor.LastModified = new DateTime(1970, 1, 1);
                uint hash = RSYNCHIT;
                int n;
                while ((n = sourceStream.Read(buffer, 0, BUFFER_SIZE)) > 0)
                {
                    for (int i = 0, j = 0; i < n; i++)
                    {
                        hash = ((hash << 1) ^ buffer[i]) & RSYNCMASK;

                        if (hash == RSYNCHIT)
                        {
                            compressor.FlushMode = FlushType.Sync;
                            compressor.Write(buffer, j, i + 1 - j);
                            j = i + 1;
                        }
                        else if (i + 1 == n)
                        {
                            compressor.FlushMode = FlushType.None;
                            compressor.Write(buffer, j, i + 1 - j);
                        }
                    }
                }
            }
        }

        public static async Task CompressAsync(Stream sourceStream, Stream destStream)
        {
            var buffer = new byte[BUFFER_SIZE];

            using (var compressor = new GZipStream(destStream, Ionic.Zlib.CompressionMode.Compress,
                       CompressionLevel.BestSpeed, true))
            {
                compressor.LastModified = new DateTime(1970, 1, 1);
                uint hash = RSYNCHIT;
                int n;
                while ((n = await sourceStream.ReadAsync(buffer, 0, BUFFER_SIZE).ConfigureAwait(false)) > 0)
                {
                    for (int i = 0, j = 0; i < n; i++)
                    {
                        hash = ((hash << 1) ^ buffer[i]) & RSYNCMASK;

                        if (hash == RSYNCHIT)
                        {
                            compressor.FlushMode = FlushType.Sync;
                            await compressor.WriteAsync(buffer, j, i + 1 - j).ConfigureAwait(false);
                            j = i + 1;
                        }
                        else if (i + 1 == n)
                        {
                            compressor.FlushMode = FlushType.None;
                            await compressor.WriteAsync(buffer, j, i + 1 - j).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
    }
}
