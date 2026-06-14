using NlsDataGenerator.IcuFormat;

namespace NlsDataGenerator.ResourceBundle;

// A key-value table, ported from genrb's TableResource. Children are kept in ascending ASCII key
// order (the runtime binary-searches them). When every value fits a 16-bit unit the table is stored
// compactly in the 16-bit pool (Table16); otherwise it becomes a 16-bit-keyed Table or a fully
// 32-bit Table32. A table that contains a binary or a sub-table cannot be Table16.
internal sealed class TableResource : Resource
{
    private Resource? _first;
    private int _count;

    // The concrete table layout chosen during the write16 pass when it is not stored as Table16.
    private int _tableType = ResourceType.Table;

    public TableResource(int key)
        : base(key)
    {
    }

    public void Add(Resource child, ResourceBundleWriter bundle)
    {
        ++_count;
        if (_first == null)
        {
            _first = child;
            child.Next = null;
            return;
        }

        var childKey = bundle.RawKeyString(child.Key);
        Resource? previous = null;
        var current = _first;
        while (current != null)
        {
            var currentKey = bundle.RawKeyString(current.Key);
            var diff = string.CompareOrdinal(currentKey, childKey);
            if (diff < 0)
            {
                previous = current;
                current = current.Next;
            }
            else
            {
                // diff > 0: insert before current (diff == 0 would be a duplicate key, rejected by
                // construction since each table gets distinct tags).
                if (previous == null)
                {
                    _first = child;
                }
                else
                {
                    previous.Next = child;
                }
                child.Next = current;
                return;
            }
        }
        previous!.Next = child;
        child.Next = null;
    }

    public override void CollectKeys(Action<int> collector)
    {
        collector(Key);
        for (var current = _first; current != null; current = current.Next)
        {
            current.CollectKeys(collector);
        }
    }

    protected override void HandlePreflightStrings(ResourceBundleWriter bundle)
    {
        for (var current = _first; current != null; current = current.Next)
        {
            current.PreflightStrings(bundle);
        }
    }

    protected override void HandleWrite16(ResourceBundleWriter bundle)
    {
        if (_count == 0)
        {
            Res = ResourceType.MakeEmptyResource(ResourceType.Table);
            Written = true;
            return;
        }

        var key16 = 0;
        var res16 = 0;
        for (var current = _first; current != null; current = current.Next)
        {
            current.Write16(bundle);
            key16 |= current.Key16;
            res16 |= current.Res16;
        }
        if (_count > bundle.MaxTableLength)
        {
            bundle.MaxTableLength = _count;
        }

        if (_count <= 0xffff && key16 >= 0)
        {
            if (res16 >= 0)
            {
                Res = ResourceType.MakeResource(ResourceType.Table16, bundle.PoolLength);
                bundle.AppendPoolUnit((ushort)_count);
                for (var current = _first; current != null; current = current.Next)
                {
                    bundle.AppendPoolUnit((ushort)current.Key16);
                }
                for (var current = _first; current != null; current = current.Next)
                {
                    bundle.AppendPoolUnit((ushort)current.Res16);
                }
                Written = true;
            }
            else
            {
                _tableType = ResourceType.Table;
            }
        }
        else
        {
            _tableType = ResourceType.Table32;
        }
    }

    protected override void HandlePreWrite(ref uint byteOffset)
    {
        for (var current = _first; current != null; current = current.Next)
        {
            current.PreWrite(ref byteOffset);
        }
        if (_tableType == ResourceType.Table)
        {
            Res = ResourceType.MakeResource(ResourceType.Table, (int)(byteOffset >> 2));
            byteOffset += 2 + (uint)_count * 6;
        }
        else
        {
            Res = ResourceType.MakeResource(ResourceType.Table32, (int)(byteOffset >> 2));
            byteOffset += 4 + (uint)_count * 8;
        }
    }

    protected override void HandleWrite(LittleEndianWriter writer, ref uint byteOffset)
    {
        for (var current = _first; current != null; current = current.Next)
        {
            current.Write(writer, ref byteOffset);
        }
        if (_tableType == ResourceType.Table)
        {
            writer.WriteUInt16((ushort)_count);
            for (var current = _first; current != null; current = current.Next)
            {
                writer.WriteUInt16((ushort)current.Key16);
            }
            byteOffset += (1 + (uint)_count) * 2;
            if ((_count & 1) == 0)
            {
                writer.WriteFiller(2);
                byteOffset += 2;
            }
        }
        else
        {
            writer.WriteUInt32((uint)_count);
            for (var current = _first; current != null; current = current.Next)
            {
                writer.WriteUInt32((uint)current.Key);
            }
            byteOffset += (1 + (uint)_count) * 4;
        }
        for (var current = _first; current != null; current = current.Next)
        {
            writer.WriteUInt32(current.Res);
        }
        byteOffset += (uint)_count * 4;
    }
}
