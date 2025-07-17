using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using PlanningDES;
using UltraDES;
using static System.Runtime.InteropServices.JavaScript.JSType;
using TimeContext = System.ValueTuple<System.Collections.Immutable.ImmutableList<UltraDES.AbstractEvent>, PlanningDES.Scheduler, PlanningDES.Restriction, float>;
using Context = System.ValueTuple<System.Collections.Immutable.ImmutableList<UltraDES.AbstractEvent>, PlanningDES.Restriction, float>;
using UltraDES.PetriNets;
using System.Collections.Concurrent;

namespace PlanningAlgorithms
{
    public static partial class Algorithms
    {
        public static AbstractEvent[] SCO(ISchedulingProblem problem, int products, int limiar = 100, bool controllableFirst = true)
        {
            var initial = problem.InitialState;
            var target = problem.TargetState;
            var resOrig = problem.InitialRestrition(products);
            var depth = products * problem.Depth;
            var uncontrollables = problem.Events.Where(e => !e.IsControllable).ToSet();
            var transitions = problem.Transitions;

            IDictionary<AbstractState, Context> frontier = new Dictionary<AbstractState, Context> { { initial, (ImmutableList<AbstractEvent>.Empty, resOrig, 0f) } };

            for (var i = 0; i < depth; i++)
            {
                var newFrontier = new ConcurrentDictionary<AbstractState, Context>();

                void Loop(KeyValuePair<AbstractState, Context> kvp)
                {
                    var (q1, (sequence1, res1, parallelism1)) = kvp;

                    var events = res1.Enabled;
                    events.UnionWith(uncontrollables);
                    events.IntersectWith(transitions[q1].Keys);

                    if (controllableFirst && events.Any(e => e.IsControllable)) events.ExceptWith(uncontrollables);

                    foreach (var e in events)
                    {
                        var q2 = transitions[q1][e];

                        var parallelism2 = parallelism1 + q2.ActiveTasks();
                        var sequence2 = sequence1.Add(e);
                        var res2 = e.IsControllable ? res1.Update(e) : res1;
                        var context2 = (sequence2, res2, parallelism2);

                        newFrontier.AddOrUpdate(q2, context2, (oldq, oldc) => oldc.Item3 < parallelism2 ? context2 : oldc);

                    }
                }

                if (frontier.Count > limiar) Partitioner.Create(frontier, EnumerablePartitionerOptions.NoBuffering).AsParallel().ForAll(Loop);
                else foreach (var kvp in frontier) Loop(kvp);

                frontier = newFrontier;

                Debug.WriteLine($"Frontier: {frontier.Count} elements");

            }

            if (!frontier.ContainsKey(target))
                throw new Exception($"The algorithm could not reach the targer ({target})");

            //return frontier[target].Item1.ToArray();

            var controllables = frontier[target].Item1.Where(e => e.IsControllable).ToArray();

            int initialSize = controllables.Length / 4;
            int productionSize = (products % 4 == 0) ? 2 * initialSize + 1 : 2 * initialSize + 1;

            var initialPhase = controllables.Take(initialSize).ToArray();
            var productionPhase = controllables.Skip(initialSize).Take(productionSize).ToArray();
            var finalPhase = controllables.Skip(initialSize + productionSize).ToArray();

            var bestSolution = VNSSCO(initialPhase, productionPhase, finalPhase, products, problem);

            return bestSolution;

        }
        static AbstractEvent[] VNSSCO(AbstractEvent[] initialPhase, AbstractEvent[] productionPhase, AbstractEvent[] finalPhase, int products, ISchedulingProblem problem)
        {
            int noImprovementCount = 0;
            int maxNoImprovement = 20;
            int iteration = 0;
            int maxIterations = 10;
            int kMax = 20;
            var array = initialPhase.Concat(productionPhase).Concat(finalPhase).ToArray();
            var (currentMakespan, currentArray) = PlanningDES.Tools.TimeEvaluationControllable(problem, array);

            while (iteration < maxIterations && noImprovementCount < maxNoImprovement)
            {
                int k = 1;
                while (k < kMax)
                {
                    (AbstractEvent[] newSolution, int[] productionSequence) = TwoOptSwapConcat(array, problem,initialPhase.Length);
                    var finalArray = LGSE(newSolution, problem);
                    var (newMakespan, newArray) = PlanningDES.Tools.TimeEvaluationControllable(problem, finalArray);

                    if (newMakespan < currentMakespan)
                    {
                        currentArray = newArray;
                        currentMakespan = newMakespan;
                        noImprovementCount = 0;
                        break;
                    }
                    else
                    {
                        k++;
                    }
                }
                if (k > kMax)
                {
                    noImprovementCount++;
                }
                iteration++;
            }

            return currentArray;
        }
    }
}
