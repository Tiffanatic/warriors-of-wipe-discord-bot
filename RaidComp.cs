namespace WarriorsOfWipeBot;

[Serializable]
public struct ContentComp(int tanks, int healers, int dps)
{
    public int Tanks = tanks;
    public int Healers = healers;
    public int Dps = dps;
    public readonly int Count => Tanks + Healers + Dps;
}

internal static class RaidComp
{
    private record struct CompCount(int Tanks, int Healers, int Dps, int Allrounders)
    {
        public readonly int Count => Tanks + Healers + Dps + Allrounders;

        public readonly bool Valid(ContentComp contentComp) =>
            Tanks <= contentComp.Tanks &&
            Healers <= contentComp.Healers &&
            Dps <= contentComp.Dps &&
            Count <= contentComp.Count;

        public void Add(RaidDataMember member)
        {
            switch (member.JobData?.RoleType)
            {
                case RoleType.Tank:
                    Tanks++;
                    break;
                case RoleType.Healer:
                    Healers++;
                    break;
                case RoleType.Dps:
                    Dps++;
                    break;
                case RoleType.AllRounder:
                    Allrounders++;
                    break;
                case null:
                    break;
            }
        }
    }

    public static IEnumerable<string> FormatPlayerList(List<RaidDataMember> members, ContentComp contentComp,
        bool requiresMentor)
    {
        CompCount count = new();
        var hasMentor = false;
        foreach (var member in members)
        {
            if (!member.Helper)
                count.Add(member);
            if (member.Mentor)
                hasMentor = true;
        }

        var full = count.Count == contentComp.Count;
        var memberIndex = 0;

        // TODO: Ignore helpers?
        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Tank)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }

        if (!full)
            for (var i = count.Tanks; i < contentComp.Tanks; i++)
                yield return Raid.TankEmote + " " + Raid.PlaceholderDash;

        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Healer)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }

        if (!full)
            for (var i = count.Healers; i < contentComp.Healers; i++)
                yield return Raid.HealerEmote + " " + Raid.PlaceholderDash;

        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Dps)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }

        if (!full)
            for (var i = count.Dps; i < contentComp.Dps; i++)
                yield return Raid.DpsEmote + " " + Raid.PlaceholderDash;

        while (memberIndex < members.Count)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }

        if (requiresMentor && !hasMentor)
        {
            yield return Raid.crown + " needed";
        }
    }

    public static bool CanAddPlayer(List<RaidDataMember> members, RaidDataMember toAdd, ContentComp contentComp,
        ulong ignoreId, bool requiresMentor)
    {
        if (toAdd.Helper)
            return true;
        CompCount count = new();
        var hasMentor = false;
        foreach (var member in members)
        {
            if (!member.Helper && member.UserId != ignoreId)
            {
                count.Add(member);
                if (toAdd.JobData?.DuplicatesAllowed == false && member.JobData?.Id == toAdd.JobData?.Id)
                    return false;
                if (member.Mentor)
                    hasMentor = true;
            }
        }

        count.Add(toAdd);
        if (requiresMentor && !hasMentor && !toAdd.Mentor && count.Count == contentComp.Count)
            return false;
        return count.Valid(contentComp);
    }
}
