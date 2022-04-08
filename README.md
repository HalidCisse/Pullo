# Pullo
High performance parallel task runner

````csharp
    class Program
    {
        static void Main(string[] args)
        {
            var cancellationToken = CancellationToken.None;
            var pullo             = new Pullo()
                .WithMaxDegreeOfParallelism(100)
                .With(cancellationToken) 
                .WithTimeout(TimeSpan.FromHours(1), 5); // timeout and retry count

            pullo.OnStart((state, action)    => Console.WriteLine("Action Started"));
            pullo.OnSuccess((state, action)  => Console.WriteLine("Action Successful"));
            pullo.OnError((state, action, e) => Console.WriteLine("Action Errored {0}", e.Message ));
            
            pullo.Enqueue(token => Console.WriteLine("Hello Task Enqueue 1!"));
            pullo.Enqueue(token => Console.WriteLine("Hello Task Enqueue 2!"));

            pullo.Run(new List<Action<CancellationToken>>
            {
                token => Console.WriteLine("Hello Task One!"),
                token => Console.WriteLine("Hello Task 2!"),
                token => Console.WriteLine("Hello Task 3!"),
                token => Console.WriteLine("Hello Task 4!")
            });
            
            await pullo.Wait(); // Run all the jobs

            //pullo.Size();      // Size of queue
            //pullo.Stop();      // Stop processing and cancel all jobs

            Console.WriteLine("Hello pullo!");
        }
    }
````
