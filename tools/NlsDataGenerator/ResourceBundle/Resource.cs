using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.ResourceBundle;

// One node in the resource-bundle tree, ported from genrb's SResource. The write happens in passes
// (preflightStrings, write16, preWrite, write) over the whole tree; each node carries the mutable
// state those passes fill in (its resource word, key offset, and whether it has been written yet).
internal abstract class Resource
{
    // Offset of this node's key string in the raw (pre-compaction) key pool, or -1 for keyless
    // nodes (the root table). compactKeys() rewrites this to the final, deduplicated offset.
    public int Key = -1;

    // The key offset truncated to a 16-bit table key, or -1 if it does not fit.
    public int Key16 = -1;

    // The 32-bit resource word: type tag plus offset/value. Bogus until a pass computes it.
    public uint Res = ResourceType.Bogus;

    // The resource word reduced to a 16-bit unit offset for use inside a Table16/Array16, or -1.
    public int Res16 = -1;

    // Set once this node's bytes (or 16-bit units) have been emitted.
    public bool Written;

    // Sibling link within a container, kept in ascending key order.
    public Resource? Next;

    protected Resource(int key)
    {
        Key = key;
    }

    public virtual void CollectKeys(Action<int> collector)
    {
        collector(Key);
    }

    public void PreflightStrings(ResourceBundleWriter bundle)
    {
        // A precomputed word (integer, or an empty string/binary) needs no string data.
        if (Res != ResourceType.Bogus)
        {
            return;
        }
        HandlePreflightStrings(bundle);
    }

    protected virtual void HandlePreflightStrings(ResourceBundleWriter bundle)
    {
    }

    public void Write16(ResourceBundleWriter bundle)
    {
        if (Key >= 0)
        {
            // compactKeys() built a map from the parsed key offset to the final one; negative
            // results denote shared pool-bundle keys, which this writer never produces.
            Key = bundle.MapKey(Key);
            if (Key >= 0 && Key < bundle.LocalKeyLimit)
            {
                Key16 = Key;
            }
        }
        if (Res == ResourceType.Bogus)
        {
            HandleWrite16(bundle);
        }
        Res16 = bundle.MakeRes16(Res);
    }

    protected virtual void HandleWrite16(ResourceBundleWriter bundle)
    {
    }

    public void PreWrite(ref uint byteOffset)
    {
        if (Res != ResourceType.Bogus)
        {
            return;
        }
        HandlePreWrite(ref byteOffset);
        byteOffset += ResourceBundleWriter.CalcPadding(byteOffset);
    }

    protected abstract void HandlePreWrite(ref uint byteOffset);

    public void Write(LittleEndianWriter writer, ref uint byteOffset)
    {
        if (Written)
        {
            return;
        }
        HandleWrite(writer, ref byteOffset);
        var padding = ResourceBundleWriter.CalcPadding(byteOffset);
        if (padding > 0)
        {
            writer.WriteFiller((int)padding);
            byteOffset += padding;
        }
        Written = true;
    }

    protected abstract void HandleWrite(LittleEndianWriter writer, ref uint byteOffset);
}
