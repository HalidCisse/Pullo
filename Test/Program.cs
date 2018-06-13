using System;
using System.Threading;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            var cancellationToken = CancellationToken.None;
            var pullo             = new Pullo();
                
            pullo.WithMaxDegreeOfParallelism(2)
                .With(cancellationToken)
                .WithTimeout(TimeSpan.FromHours(1), 5);

            pullo.OnStart((state, action) =>
            {
                Console.WriteLine("Action Started");
            });

            pullo.OnSuccess((state, action) =>
            {
                Console.WriteLine("Action Successful");
            });

            pullo.OnError((state, action, e) =>
            {
                Console.WriteLine("Action Errored {0}", e.Message );
            });

            //pullo.IsCompleted
            //pullo.Done();
            //pullo.Enqueue();
            //pullo.Size();
            //pullo.Stop();
            
            pullo.StartAndWait();
            Console.WriteLine("Hello pullo!");
        }
    }
}
