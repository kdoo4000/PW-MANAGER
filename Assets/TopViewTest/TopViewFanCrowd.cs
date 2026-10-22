using System;
using System.Linq;
using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;

public sealed class TopViewFanCrowdFrame
{
    public readonly float[] Heat = new float[3];
    public readonly int[] Focus = { -1, -1, -1 };
    public readonly float[,] Support = { { .5f, .5f }, { .5f, .5f }, { .5f, .5f } };
}

public static class TopViewFanCrowd
{
    public static FanResponseState Response(WrestlerState wrestler, int group) => group == 0 ? wrestler.FanReaction.MarkResponse :
        group == 1 ? wrestler.FanReaction.CasualResponse : wrestler.FanReaction.HardcoreResponse;

    public static float Affinity(MatchEngineEvent e, WrestlerState wrestler, int group)
    {
        var rules = MatchTechniques.Find(e)?.Rules;
        bool technical = rules?.Kind == MoveKind.Submission || rules?.MotionProfile is MoveMotionProfile.Chain or MoveMotionProfile.Hold or MoveMotionProfile.TieUp ||
            e.Type is MatchActionType.Submission or MatchActionType.Chain or MatchActionType.LimbAttack;
        bool spectacle = rules?.Kind == MoveKind.Aerial || e.Type is MatchActionType.Dive or MatchActionType.TopRopeAttack || e.Impact >= 10;
        bool drama = e.Type is MatchActionType.Counter or MatchActionType.Finisher or MatchActionType.Finish ||
            e.Result is MatchActionResult.NearFall or MatchActionResult.Countered or MatchActionResult.Escaped;
        float performance = group == 0 ? (wrestler.Roster.Alignment == KayfabeAlignment.Heel ? wrestler.Attributes.HeelWork : wrestler.Attributes.FaceWork) / 20f :
            group == 1 ? wrestler.Attributes.Charisma / 20f : wrestler.Attributes.RingPsychology / 20f;
        float content = group == 0 ? drama ? 1 : technical ? .3f : .6f :
            group == 1 ? spectacle ? 1 : technical ? .25f : drama ? .85f : .5f : technical ? 1 : spectacle ? .65f : .55f;
        if (group == 2) content *= Mathf.Clamp01(e.ExecutionQuality) * .7f + .3f;
        if (e.PhysicalExecutionFailed || e.Result == MatchActionResult.ExecutionFailure) content *= group == 2 ? .15f : .45f;
        float weight = group == 0 ? .7f : group == 1 ? .8f : .95f;
        return Mathf.Clamp01(content * weight + performance * (1 - weight));
    }

    public static TopViewFanCrowdFrame[] Build(MatchEngineState engine, WrestlerState[] wrestlers)
    {
        var frames = new TopViewFanCrowdFrame[engine.Events.Count];
        var heat = new float[3];
        var focus = new[] { -1, -1, -1 };
        var focusTime = new float[3];
        var blueShare = new[] { .5f, .5f, .5f };
        for (int i = 0; i < engine.Events.Count; i++)
        {
            var e = engine.Events[i];
            string id = e.Result is MatchActionResult.Countered or MatchActionResult.Escaped ? e.ReceiverId : e.PerformerId;
            int actor = engine.Participants.FindIndex(p => p.Id == id);
            bool meaningful = e.CrowdReaction != 0 || e.IsPhysicalAttack || e.Type is MatchActionType.Submission or MatchActionType.Chain;
            frames[i] = new TopViewFanCrowdFrame();
            for (int group = 0; group < 3; group++)
            {
                if (actor >= 0 && meaningful)
                {
                    float affinity = Affinity(e, wrestlers[actor], group);
                    var fan = Response(wrestlers[actor], group);
                    float interest = Mathf.Clamp01(fan.Interest / 100);
                    float response = Mathf.Clamp(e.After.CrowdHeat * (.35f + affinity) + e.CrowdReaction * affinity * 3 +
                        interest * affinity * 12, 0, 100);
                    response = Mathf.Clamp(response * (.75f + Mathf.Clamp01(fan.Preference / 100) * .5f), 0, 100);
                    heat[group] = Mathf.Lerp(heat[group], response, .45f);
                    float attention = affinity * .75f + interest * .15f + Mathf.Clamp01(fan.Preference / 100) * .1f;
                    if (attention >= .55f || focus[group] < 0 || e.MatchTimeSeconds - focusTime[group] > 20)
                    { focus[group] = actor; focusTime[group] = e.MatchTimeSeconds; }
                }
                else heat[group] = Mathf.MoveTowards(heat[group], e.After.CrowdHeat * .4f, .4f);
                frames[i].Heat[group] = heat[group];
                frames[i].Focus[group] = focus[group];
                var weights = new float[2];
                for (int side = 0; side < 2; side++)
                {
                    var fan = Response(wrestlers[side], group);
                    weights[side] = .1f + Mathf.Clamp01(fan.Interest / 100) * .6f + Mathf.Clamp01(fan.Preference / 100) * .4f;
                    if (meaningful && actor == side) weights[side] *= 1 + Affinity(e, wrestlers[side], group) * .8f;
                }
                blueShare[group] = Mathf.Lerp(blueShare[group], weights[0] / (weights[0] + weights[1]), .35f);
                frames[i].Support[group, 0] = blueShare[group];
                frames[i].Support[group, 1] = 1 - blueShare[group];
            }
        }
        return frames;
    }
}
