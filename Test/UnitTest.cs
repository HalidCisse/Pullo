Public class Test
{

        [Fact]
        public async Task Runner_WhenTasksAdded_WillRunTasks()
        {
            // Arrange
            var task1IsRun = false;
            var task2IsRun = false;
            var completedTasks = 0;

            var tasks = new List<Func<Task>>
            {
                () =>
                {
                    task1IsRun = true;
                    return Task.CompletedTask;
                },
                () =>
                {
                    task2IsRun = true;
                    return Task.CompletedTask;
                },
            };

            // Act
            await tasks.RunAsync(
                body: func => func(), 
                maxThreadCount: 2, 
                onTaskCompleted:(func => completedTasks++), 
                token: CancellationToken.None);

            // Asset
            Assert.True(task1IsRun);
            Assert.Equal(2, completedTasks);
        }

        [Fact]
        public async Task Runner_WhenTasksAdded_WillRespectMaxThreadCount()
        {
            // Arrange
            DateTime task1CompleteTime, task2CompleteTime;
            task1CompleteTime = task2CompleteTime= DateTime.Today;

            var tasks = new List<Func<Task>>
            {
                () =>
                {
                    task1CompleteTime = DateTime.UtcNow;
                    return Task.CompletedTask;
                },
                () =>
                {
                    task2CompleteTime = DateTime.UtcNow;
                    return Task.CompletedTask;
                },
            };

            // Act
            await tasks.RunAsync(
                body: func => func(), 
                maxThreadCount: 1, // we expect task to be executed one by one
                token: CancellationToken.None);

            // Asset
            task2CompleteTime.Should().BeAfter(task1CompleteTime);
        }

        [Fact]
        public async Task Runner_WhenCancelled_WontThrow()
        {
            // Arrange
            var tasks = new List<Func<Task>>
            {
                () =>
                {
                    throw new OperationCanceledException();
                },
                () =>
                {
                    throw new TimeoutException();
                },
                () =>
                {
                    throw new TaskCanceledException();
                },
            };

            // Act
            var exception = await Record.ExceptionAsync(async () => 
                await tasks.RunAsync(
                    body: func => func(),
                    maxThreadCount: 2,
                    token: CancellationToken.None));
            
            // Asset
            Assert.Null(exception);
        }

        [Fact]
        public async Task Runner_WhenTasksThrow_WillThrowAggregateException()
        {
            // Arrange
            var tasks = new List<Func<Task>>
            {
                () =>
                {
                    throw new Exception("exception");
                },
                () =>
                {
                    return Task.CompletedTask;
                },
            };

            // Act
            var recordException = await Record.ExceptionAsync(async () => 
                await tasks.RunAsync(
                    body: func => func(),
                    maxThreadCount: 2,
                    token: CancellationToken.None));
            
            // Asset
            Assert.NotNull(recordException);
            Assert.IsType<AggregateException>(recordException);
        }
}
