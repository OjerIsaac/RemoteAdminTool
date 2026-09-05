using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteAdminTool.Shared;

/// <summary>
/// Chunked file transfer over an existing message stream.
/// Sends files in 64KB chunks with progress reporting and SHA256 checksum verification.
/// Used by both Host and Viewer sides.
/// </summary>
public sealed class FileTransferService
{
    private const int ChunkSize = 64 * 1024; // 64KB chunks

    /// <summary>
    /// Send a file over the stream in chunks.
    /// </summary>
    public async Task SendFileAsync(Stream networkStream, string filePath,
        string? destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File not found", filePath);

        var transferId = Guid.NewGuid().ToString("N")[..8];
        var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / ChunkSize);

        // Send transfer request
        var request = new FileTransferRequestMessage(
            transferId, fileInfo.Name, fileInfo.Length, destinationPath);
        await MessageFraming.WriteFrameAsync(networkStream, MessageType.FileTransferRequest,
            JsonSerializer.SerializeToUtf8Bytes(request), ct);

        // Read file and send in chunks
        using var fileStream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var buffer = new byte[ChunkSize];
        int chunkIndex = 0;

        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

            var chunkData = bytesRead == ChunkSize ? buffer : buffer[..bytesRead];
            var chunk = new FileTransferDataMessage(transferId, chunkIndex, totalChunks, chunkData);
            await MessageFraming.WriteFrameAsync(networkStream, MessageType.FileTransferData,
                JsonSerializer.SerializeToUtf8Bytes(chunk), ct);

            chunkIndex++;
            progress?.Report((double)chunkIndex / totalChunks);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var checksum = Convert.ToHexString(sha256.Hash!);

        // Send completion
        var complete = new FileTransferCompleteMessage(transferId, checksum);
        await MessageFraming.WriteFrameAsync(networkStream, MessageType.FileTransferComplete,
            JsonSerializer.SerializeToUtf8Bytes(complete), ct);
    }

    /// <summary>
    /// Receive a file transfer. Call this when you get a FileTransferRequest message.
    /// Returns the path where the file was saved.
    /// </summary>
    public async Task<string> ReceiveFileAsync(Stream networkStream, FileTransferRequestMessage request,
        string saveDirectory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(saveDirectory);
        var savePath = Path.Combine(saveDirectory, SanitizeFileName(request.FileName));

        // Avoid overwriting — append number if file exists
        var baseName = Path.GetFileNameWithoutExtension(savePath);
        var extension = Path.GetExtension(savePath);
        int counter = 1;
        while (File.Exists(savePath))
        {
            savePath = Path.Combine(saveDirectory, $"{baseName} ({counter}){extension}");
            counter++;
        }

        using var fileStream = File.Create(savePath);
        using var sha256 = SHA256.Create();

        while (!ct.IsCancellationRequested)
        {
            var (type, payload) = await MessageFraming.ReadFrameAsync(networkStream, ct);

            if (type == MessageType.FileTransferData)
            {
                var chunk = JsonSerializer.Deserialize<FileTransferDataMessage>(payload);
                if (chunk.TransferId != request.TransferId) continue;

                sha256.TransformBlock(chunk.Data, 0, chunk.Data.Length, null, 0);
                await fileStream.WriteAsync(chunk.Data, ct);

                progress?.Report((double)(chunk.ChunkIndex + 1) / chunk.TotalChunks);
            }
            else if (type == MessageType.FileTransferComplete)
            {
                var complete = JsonSerializer.Deserialize<FileTransferCompleteMessage>(payload);
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var localChecksum = Convert.ToHexString(sha256.Hash!);

                if (localChecksum != complete.Checksum)
                {
                    Console.Error.WriteLine($"[FileTransfer] Checksum mismatch! Expected {complete.Checksum}, got {localChecksum}");
                }

                break;
            }
            else if (type == MessageType.FileTransferCancel)
            {
                fileStream.Close();
                File.Delete(savePath);
                throw new OperationCanceledException("File transfer cancelled by remote side.");
            }
        }

        return savePath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
