namespace SuperSocketLite.Common;

/// <summary>
/// Fixed-size accumulation buffer used by the CollectSend feature.
/// </summary>
public class ReuseLockBaseBuffer
{
    Int32 ReadPos = 0;
    Int32 WritePos = 0;
    Int32 BufferSize = 0;
    byte[] mBuffer = null!;

    Int32 MinumBufferSize = 0;

    public ReuseLockBaseBuffer(int bufferSize)
    {
        BufferSize = bufferSize;
        mBuffer = new byte[bufferSize];
        MinumBufferSize = BufferSize / 4;
    }

    public bool Copy(byte[] source, int pos, int count)
    {
        lock(mBuffer)
        {
            var expectedLength = WritePos + count;
            if(BufferSize <= expectedLength)
            {
                return false;
            }

            Buffer.BlockCopy(source, pos, mBuffer, WritePos, count);
            WritePos += count;
        }

        return true;
    }

    // 1개의 스레드에서만 호출해야 한다.
    public ArraySegment<byte> GetData()
    {
        lock (mBuffer)
        {
            var size = WritePos - ReadPos;
            return new ArraySegment<byte>(mBuffer, ReadPos, size);
        }
    }

    /// <summary>
    /// Marks the first <paramref name="size"/> bytes of the current data as consumed.
    /// </summary>
    public void Commit(int size)
    {
        if (size <= 0)
            return;

        lock (mBuffer)
        {
            var currentDataSize = WritePos - ReadPos;

            //Consuming everything (or more than is there) resets the buffer to the front.
            if (size >= currentDataSize)
            {
                ReadPos = 0;
                WritePos = 0;
                return;
            }

            ReadPos += size;
            currentDataSize = WritePos - ReadPos;

            //Buffer.BlockCopy has memmove semantics, so the leftover bytes can be slid to the
            //front with a single copy even though source and destination overlap - no temp array.
            if (currentDataSize < ReadPos || MinumBufferSize < BufferSize - WritePos)
            {
                Buffer.BlockCopy(mBuffer, ReadPos, mBuffer, 0, currentDataSize);
                ReadPos = 0;
                WritePos = currentDataSize;
            }
        }
    }
}
