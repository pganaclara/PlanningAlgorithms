using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PlanningDES;
using UltraDES;
using TimeContext = System.ValueTuple<System.Collections.Immutable.ImmutableList<UltraDES.AbstractEvent>, PlanningDES.Scheduler, PlanningDES.Restriction, float>;

namespace PlanningAlgorithms
{
    public static partial class Algorithms
    {
        public static AbstractEvent[] SCO(ISchedulingProblem problem, int products, int limiar = 100, bool controllableFirst = true)
        {
            var initial = problem.InitialState;

            var target = problem.TargetState;
            var schOrig = problem.InitialScheduler;
            var resOrig = problem.InitialRestrition(products);
            var depth = products * problem.Depth;
            var uncontrollables = problem.Events.Where(e => !e.IsControllable).ToSet();
            var transitions = problem.Transitions;

            var frontier = new Dictionary<AbstractState, List<TimeContext>>
            {
                {initial, new List<TimeContext>{(ImmutableList<AbstractEvent>.Empty, schOrig, resOrig, 0f)}}
            };

            if (!frontier.ContainsKey(target))
                throw new Exception($"The algorithm could not reach the targer ({target})");

            return frontier[target].OrderBy(c => c.Item4).First().Item1.ToArray();
        }
    }
}
