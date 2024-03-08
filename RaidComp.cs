namespace WarriorsOfWipeBot;

[Serializable]
public struct ContentComp(int tanks, int healers, int dps)
{
    public int Tanks = tanks;
    public int Healers = healers;
    public int Dps = dps;
    public readonly int Count => Tanks + Healers + Dps;
}

internal class RaidComp
{
    private record struct CompCount(int Tanks, int Healers, int Dps, int Allrounders)
    {
        public readonly int Count => Tanks + Healers + Dps + Allrounders;
        public readonly bool Valid(ContentComp contentComp) => Tanks <= contentComp.Tanks && Healers <= contentComp.Healers && Dps <= contentComp.Dps && Count <= contentComp.Count;

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

    public static IEnumerable<string> FormatPlayerList(List<RaidDataMember> members, ContentComp contentComp)
    {
        CompCount count = new();
        foreach (var member in members)
            if (!member.Helper)
                count.Add(member);
        var full = count.Count == contentComp.Count;
        int memberIndex = 0;

        // TODO: Ignore helpers?
        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Tank)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }
        if (!full)
            for (int i = count.Tanks; i < contentComp.Tanks; i++)
                yield return Raid.TankEmote + " ---";

        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Healer)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }
        if (!full)
            for (int i = count.Healers; i < contentComp.Healers; i++)
                yield return Raid.HealerEmote + " ---";

        while (memberIndex < members.Count && members[memberIndex].JobData?.RoleType == RoleType.Dps)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }
        if (!full)
            for (int i = count.Dps; i < contentComp.Dps; i++)
                yield return Raid.DpsEmote + " ---";

        while (memberIndex < members.Count)
        {
            yield return Raid.FormatMember(members[memberIndex]);
            memberIndex++;
        }
    }

    public static bool CanAddPlayer(List<RaidDataMember> members, RaidDataMember toAdd, ContentComp contentComp, ulong ignoreId)
    {
        if (toAdd.Helper)
            return true;
        CompCount count = new();
        foreach (var member in members)
            if (!member.Helper && member.UserId != ignoreId)
                count.Add(member);
        count.Add(toAdd);
        return count.Valid(contentComp);
    }
}
