using System.Text;

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

        public void Add(Job member)
        {
            switch (member.RoleType)
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
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private record struct RenderedMember(RaidDataMember? Member, List<Job> Jobs);

    public static List<string> FormatPlayerList(List<RaidDataMember> members, ContentComp contentComp,
        bool requiresMentor)
    {
        var iterator = new CompIterator(members, contentComp);
        iterator.Compute();
        var sorted = new List<RenderedMember>(members.Count);
        for (var memberIndex = 0; memberIndex < iterator.Members.Count; memberIndex++)
        {
            var m = iterator.Members[memberIndex];
            var acceptableJobs = iterator.AcceptableJobs[memberIndex];
            var jobList = m.JobData.Where((_, jobIndex) => acceptableJobs[jobIndex]).ToList();
            sorted.Add(new RenderedMember(m, jobList));
        }

        sorted.Sort((l, r) =>
        {
            var count = Math.Min(l.Jobs.Count, r.Jobs.Count);
            for (int i = 0; i < count; i++)
            {
                // oof, runtime
                var cmp = Raid.IndexOfJob(l.Jobs[i]).CompareTo(Raid.IndexOfJob(r.Jobs[i]));
                if (cmp != 0)
                    return cmp;
            }

            if (l.Jobs.Count != r.Jobs.Count)
                return l.Jobs.Count.CompareTo(r.Jobs.Count);
            return string.Compare(l.Member!.Nick, r.Member!.Nick, StringComparison.Ordinal);
        });

        var dpsIndex = InsertIndexFor(RoleType.Dps);
        for (var i = 0; i < iterator.DpsSlotsAvailable; i++)
            sorted.Insert(dpsIndex, new RenderedMember(null, [Raid.JobFromId("DPS")!.Value]));
        var healerIndex = InsertIndexFor(RoleType.Healer);
        for (var i = 0; i < iterator.HealerSlotsAvailable; i++)
            sorted.Insert(healerIndex, new RenderedMember(null, [Raid.JobFromId("HLR")!.Value]));
        var tankIndex = InsertIndexFor(RoleType.Tank);
        for (var i = 0; i < iterator.TankSlotsAvailable; i++)
            sorted.Insert(tankIndex, new RenderedMember(null, [Raid.JobFromId("TNK")!.Value]));

        int InsertIndexFor(RoleType roleType)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                var jobs = sorted[i].Jobs;
                if (jobs.Count > 0 && jobs[0].RoleType > roleType)
                    return i;
            }

            return sorted.Count;
        }

        StringBuilder sb = new();
        List<string> result = [];
        foreach (var item in sorted)
        {
            sb.Clear();
            foreach (var job in item.Jobs)
                sb.Append(job.Emote);
            sb.Append(' ');
            if (item.Member != null)
            {
                sb.Append(Raid.FormatMemberNoJob(item.Member));
                if (item.Member.JobData.Count > item.Jobs.Count)
                {
                    sb.Append(" (~");
                    foreach (var job in item.Member.JobData)
                        if (!item.Jobs.Contains(job))
                            sb.Append(job.Emote);
                    sb.Append(')');
                }
            }
            else
                sb.Append(Raid.PlaceholderDash);

            result.Add(sb.ToString());
        }

        if (requiresMentor && !HasMentor(members, out _))
            result.Add(Raid.crown + " needed");

        return result;
    }

    public static bool CanAddPlayer(List<RaidDataMember> members, RaidDataMember toAdd, ContentComp contentComp,
        ulong ignoreId, bool requiresMentor)
    {
        if (toAdd.Helper)
            return true;

        List<RaidDataMember> memberList = [..members];
        for (var i = memberList.Count - 1; i >= 0; i--)
            if (memberList[i].UserId == ignoreId)
                memberList.RemoveAt(i);
        memberList.Add(toAdd);

        if (requiresMentor && !HasMentor(members, out var count) && count == contentComp.Count)
            return false;

        var iterator = new CompIterator(memberList, contentComp);
        if (!iterator.Compute())
            return false;
        return true;
    }

    private static bool HasMentor(List<RaidDataMember> members, out int memberCount)
    {
        var hasMentor = false;
        memberCount = 0;
        foreach (var member in members)
        {
            if (member.Helper)
                continue;
            if (member.Mentor)
                hasMentor = true;
            memberCount++;
        }

        return hasMentor;
    }

    private struct CompIterator
    {
        public readonly List<RaidDataMember> Members;
        public readonly List<bool[]> AcceptableJobs;
        private readonly List<Job> _choicesScratch = [];
        private readonly ContentComp _contentComp;
        public int TankSlotsAvailable;
        public int HealerSlotsAvailable;
        public int DpsSlotsAvailable;

        public CompIterator(List<RaidDataMember> members, ContentComp contentComp)
        {
            Members = members;
            _contentComp = contentComp;
            AcceptableJobs = new List<bool[]>();
            foreach (var member in members)
                AcceptableJobs.Add(new bool[member.JobData.Count]);
        }

        public bool Compute()
        {
            CompCount compCount = new();
            return Iterate(0, in compCount);
        }

        private bool Iterate(int index, in CompCount compCount)
        {
            if (index >= Members.Count)
            {
                var totalSlotsAvailable = _contentComp.Count - compCount.Count;
                TankSlotsAvailable = Math.Max(TankSlotsAvailable, Math.Min(totalSlotsAvailable, _contentComp.Tanks - compCount.Tanks));
                HealerSlotsAvailable = Math.Max(HealerSlotsAvailable, Math.Min(totalSlotsAvailable, _contentComp.Healers - compCount.Healers));
                DpsSlotsAvailable = Math.Max(DpsSlotsAvailable, Math.Min(totalSlotsAvailable, _contentComp.Dps - compCount.Dps));
                return true;
            }

            var member = Members[index];
            if (member.Helper)
            {
                Iterate(index + 1, in compCount);
                return true;
            }

            var valid = false;
            for (var i = 0; i < member.JobData.Count; i++)
            {
                var job = member.JobData[i];
                var compCountCopy = compCount;
                compCountCopy.Add(job);
                if (compCountCopy.Valid(_contentComp) && !HasDupe(_choicesScratch, job))
                {
                    _choicesScratch.Add(job);
                    if (Iterate(index + 1, in compCountCopy))
                    {
                        valid = true;
                        AcceptableJobs[index][i] = true;
                    }

                    _choicesScratch.RemoveAt(_choicesScratch.Count - 1);
                }
            }

            return valid;

            static bool HasDupe(List<Job> choicesScratch, Job job)
            {
                foreach (var existing in choicesScratch)
                    if (!existing.DuplicatesAllowed && existing.Id == job.Id)
                        return true;
                return false;
            }
        }
    }
}
