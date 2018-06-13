# Pullo
High performance parallel task runner

    class Program
    {
        static void Main(string[] args)
        {
            var cancellationToken = CancellationToken.None;
            var pullo             = new Pullo()
                .WithMaxDegreeOfParallelism(2)
                .With(cancellationToken) 
                .WithTimeout(TimeSpan.FromHours(1), 5); 

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
            
            pullo.StartAndWait(); //Start running the jobs, and wait for new jobs until Done() or Stop() is called

            //pullo.IsCompleted  // all jobs are completed and .Done() is called
            //pullo.Done();      //   Marks the pullo instances as not accepting any more additions
            //pullo.Size();      // Size of queue
            //pullo.Stop();      // Stop processing and cancel all jobs

            Console.WriteLine("Hello pullo!");
        }
    }
