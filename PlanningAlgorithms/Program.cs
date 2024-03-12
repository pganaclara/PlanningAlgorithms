using System;
using PlanningDES;
using PlanningDES.Problems;
using UltraDES;
using ConsoleTables;
using static PlanningAlgorithms.Algorithms;
using System.Linq;

namespace PlanningAlgorithms
{
    class Program
    {
        static void Main()
        {
            System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.AboveNormal;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            //var problem = new SmallFactory();
            var problem = new FlexibleManufacturingSystem();
            var algorithms = new (string, Func<int, AbstractEvent[]>)[2];
            //algorithms[0] = ("Parallelism Maximization with Time Restrictions", (p) => PMT(problem, p, controllableFirst: true));
            algorithms[0] = ("Heuristic Makespan Minimization", (p) => HMM(problem, p, controllableFirst: true));
            algorithms[1] = ("Supervisory Control and Optimization", (p) => SCO(problem, p, controllableFirst: true));
            //algorithms[3] = ("Parallelism Maximization (Logic)", (p) => PML(problem, p, controllableFirst: false));

            foreach (var (name, algorithm) in algorithms)
            {
                var table = new ConsoleTable("Batch", "Time", "Makespan", "Parallelism");
                foreach (var products in new[] { 1, 5, 10, 15, 50, 100 })
                {
                    Func<AbstractEvent[]> test = () => algorithm(products);

                    var (time, sequence) = test.Timming();
                    /*nsole.Write($"\nSequence {products} products. Size {sequence.Length}: \n");
                    foreach (var e in sequence)
                    {
                        Console.Write($"{e} | ");
                    }
                    Console.Write("\n");*/
                    var makespan = problem.TimeEvaluation(sequence);
                    var parallelism = problem.MetricEvaluation(sequence, (t) => t.destination.ActiveTasks());

                    

                    table.AddRow(products, time, makespan, parallelism); 
                }

                Console.WriteLine($"\n{name}:");
                table.Write(Format.Alternative);
                Console.WriteLine();

            }

        }
    }
}
