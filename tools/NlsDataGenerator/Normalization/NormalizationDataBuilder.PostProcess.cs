namespace NlsDataGenerator.Normalization;

// The per-Norm classification pass, ported from n2builder.cpp's postProcess and its two helpers.
// After compositions are built and mappings recursively decomposed, this puts each mapping into
// canonical order, derives lead/trail combining classes and composition boundaries, and assigns
// the overall NormType that drives the norm16 encoding.
internal sealed partial class NormalizationDataBuilder
{
    // A starter with a mapping has no composition boundary after it if it combines forward (checked
    // by the caller), is deleted, has no starter in its mapping, or its last starter combines
    // forward.
    private bool MappingHasCompBoundaryAfter(ReorderingBuffer buffer, MappingKind mappingType)
    {
        if (buffer.IsEmpty)
        {
            // Maps-to-empty-string is no boundary of any kind.
            return false;
        }
        var lastStarterIndex = buffer.LastStarterIndex;
        if (lastStarterIndex < 0)
        {
            return false;
        }
        var lastIndex = buffer.Length - 1;
        if (mappingType == MappingKind.OneWay && lastStarterIndex < lastIndex
            && buffer.CcAt(lastIndex) > 1)
        {
            // One-way mapping where another combining mark can reorder before the last one.
            return false;
        }
        var starter = buffer.CharAt(lastStarterIndex);
        if (lastStarterIndex == 0 && _norms.CombinesBack(starter))
        {
            // The last starter is at the beginning of the mapping and combines backward.
            return false;
        }
        if (Hangul.IsJamoL(starter)
            || (Hangul.IsJamoV(starter) && 0 < lastStarterIndex
                && Hangul.IsJamoL(buffer.CharAt(lastStarterIndex - 1))))
        {
            // A Jamo leading consonant or LV pair combines forward only when at the end.
            return lastStarterIndex != lastIndex;
        }
        // There can be no Hangul syllable in the fully decomposed mapping.

        // Multiple starters can combine into one; find the first of the last run of starters,
        // excluding Jamos.
        var i = lastStarterIndex;
        while (0 < i && buffer.CcAt(i - 1) == 0)
        {
            var c = buffer.CharAt(i - 1);
            if (Hangul.IsJamo(c))
            {
                break;
            }
            starter = c;
            --i;
        }
        // Compose as far as possible, then see if further compositions with following characters
        // are possible.
        var starterNorm = _norms.GetNorm(starter);
        if (i == lastStarterIndex && (starterNorm is null || !starterNorm.CombinesFwd()))
        {
            return true;
        }
        byte prevCc = 0;
        while (++i < buffer.Length)
        {
            var cc = buffer.CcAt(i);
            if (i > lastStarterIndex && _norms.CombinesWithCcBetween(starterNorm!, prevCc, cc))
            {
                // The starter combines with a mark that reorders before the current one.
                return false;
            }
            var c = buffer.CharAt(i);
            int composite;
            if (starterNorm is not null && (prevCc < cc || prevCc == 0)
                && _norms.GetNormRef(c).CombinesBack && (composite = starterNorm.Combine(c)) >= 0)
            {
                // The starter combines with c into a composite replacement starter.
                starter = composite;
                starterNorm = _norms.GetNorm(starter);
                if (i >= lastStarterIndex && (starterNorm is null || !starterNorm.CombinesFwd()))
                {
                    return true;
                }
                // Keep prevCc because we "removed" the combining mark.
            }
            else if (cc == 0)
            {
                starterNorm = _norms.GetNorm(c);
                if (i == lastStarterIndex && (starterNorm is null || !starterNorm.CombinesFwd()))
                {
                    return true;
                }
                prevCc = 0;
            }
            else
            {
                prevCc = cc;
            }
        }
        if (prevCc == 0)
        {
            // Forward-combining starter at the very end.
            return false;
        }
        if (_norms.CombinesWithCcBetween(starterNorm!, prevCc, 256))
        {
            return false;
        }
        return true;
    }

    // True if the mapping by itself recomposes (it is not comp-normalized).
    private bool MappingRecomposes(ReorderingBuffer buffer)
    {
        if (buffer.LastStarterIndex < 0)
        {
            return false;
        }
        CodePointData? starterNorm = null;
        byte prevCc = 0;
        for (var i = 0; i < buffer.Length; ++i)
        {
            var c = buffer.CharAt(i);
            var cc = buffer.CcAt(i);
            if (starterNorm is not null && (prevCc < cc || prevCc == 0)
                && _norms.GetNormRef(c).CombinesBack && starterNorm.Combine(c) >= 0)
            {
                // Normal composite.
                return true;
            }
            if (cc == 0)
            {
                if (Hangul.IsJamoL(c))
                {
                    if ((i + 1) < buffer.Length && Hangul.IsJamoV(buffer.CharAt(i + 1)))
                    {
                        // Hangul syllable.
                        return true;
                    }
                    starterNorm = null;
                }
                else
                {
                    starterNorm = _norms.GetNorm(c);
                }
            }
            prevCc = cc;
        }
        return false;
    }

    private void PostProcess(CodePointData norm)
    {
        // Prerequisites: compositions are built and mappings recursively decomposed, but not yet in
        // canonical order. We do not know which code point maps to this Norm, so algorithmic deltas
        // and error reporting are deferred.
        if (norm.HasMapping())
        {
            if (norm.Mapping!.Length > Norm16Constants.MappingLengthMask)
            {
                norm.Error = "mapping longer than maximum of 31";
                return;
            }
            // Ensure canonical order.
            var buffer = new ReorderingBuffer();
            if (norm.RawMapping is not null)
            {
                norm.RawMapping = _norms.Reorder(norm.RawMapping, buffer);
                buffer.Reset();
            }
            norm.Mapping = _norms.Reorder(norm.Mapping, buffer);
            if (buffer.IsEmpty)
            {
                // A deleted character (maps to empty) gets worst-case lccc/tccc because arbitrary
                // characters on both sides become adjacent.
                norm.LeadCc = 1;
                norm.TrailCc = 0xFF;
            }
            else
            {
                norm.LeadCc = buffer.CcAt(0);
                norm.TrailCc = buffer.CcAt(buffer.Length - 1);
            }

            norm.HasCompBoundaryBefore =
                !buffer.IsEmpty && norm.LeadCc == 0 && !_norms.CombinesBack(buffer.CharAt(0));
            // No comp-boundary-after when norm combines back: a MaybeNo character whose first
            // mapping character may combine back would not recompose to this character.
            norm.HasCompBoundaryAfter =
                !norm.CombinesBack && !norm.CombinesFwd()
                && MappingHasCompBoundaryAfter(buffer, norm.Kind);

            if (norm.CombinesBack)
            {
                if (norm.Kind != MappingKind.RoundTrip)
                {
                    norm.Error = "combines-back and has a one-way mapping, "
                        + "not possible in Unicode normalization";
                }
                else if (norm.CombinesFwd())
                {
                    norm.Type = NormType.MaybeNoCombinesFwd;
                }
                else if (norm.Cc == 0)
                {
                    norm.Type = NormType.MaybeNoMappingOnly;
                }
                else
                {
                    norm.Error = "combines-back and decomposes with ccc!=0, "
                        + "not possible in Unicode normalization";
                }
            }
            else if (norm.Kind == MappingKind.RoundTrip)
            {
                norm.Type = norm.CombinesFwd() ? NormType.YesNoCombinesFwd : NormType.YesNoMappingOnly;
            }
            else
            {
                // One-way mapping.
                if (norm.CombinesFwd())
                {
                    norm.Error = "combines-forward and has a one-way mapping, "
                        + "not possible in Unicode normalization";
                }
                else if (buffer.IsEmpty)
                {
                    norm.Type = NormType.NoNoEmpty;
                }
                else if (!norm.HasCompBoundaryBefore)
                {
                    norm.Type = NormType.NoNoCompNoMaybeCc;
                }
                else if (MappingRecomposes(buffer))
                {
                    norm.Type = NormType.NoNoCompBoundaryBefore;
                }
                else
                {
                    norm.Type = NormType.NoNoCompYes;
                }
            }
        }
        else
        {
            // No mapping.
            norm.LeadCc = norm.Cc;
            norm.TrailCc = norm.Cc;

            norm.HasCompBoundaryBefore = norm.Cc == 0 && !norm.CombinesBack;
            norm.HasCompBoundaryAfter = norm.Cc == 0 && !norm.CombinesBack && !norm.CombinesFwd();

            if (norm.CombinesBack)
            {
                norm.Type = norm.CombinesFwd() ? NormType.MaybeYesCombinesFwd : NormType.MaybeYesSimple;
            }
            else if (norm.CombinesFwd())
            {
                norm.Type = NormType.YesYesCombinesFwd;
            }
            else if (norm.Cc != 0)
            {
                norm.Type = NormType.YesYesWithCc;
            }
            else
            {
                norm.Type = NormType.Inert;
            }
        }
    }
}
