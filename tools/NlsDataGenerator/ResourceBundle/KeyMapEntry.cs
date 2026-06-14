namespace NlsDataGenerator.ResourceBundle;

// One entry in compactKeys()'s remapping table: a key's original offset in the raw key pool and its
// offset after duplicate/suffix elimination and squeezing. Resources look up their final key offset
// through this map during the write16 pass.
internal struct KeyMapEntry
{
    public int OldPos;
    public int NewPos;
}
