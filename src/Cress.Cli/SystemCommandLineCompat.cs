namespace System.CommandLine
{
    using InvocationContext = Invocation.InvocationContext;

    internal static class SystemCommandLineCompat
    {
        public static void AddCommand(this Command command, Command subcommand)
            => command.Subcommands.Add(subcommand);

        public static void AddCommand(this RootCommand command, Command subcommand)
            => command.Subcommands.Add(subcommand);

        public static void AddOption(this Command command, Option option)
            => command.Options.Add(option);

        public static void AddOption(this RootCommand command, Option option)
            => command.Options.Add(option);

        public static void AddArgument(this Command command, Argument argument)
            => command.Arguments.Add(argument);

        public static void AddArgument(this RootCommand command, Argument argument)
            => command.Arguments.Add(argument);

        public static void SetHandler(this Command command, Action<InvocationContext> handler)
        {
            command.SetAction(parseResult =>
            {
                var context = new InvocationContext(parseResult);
                handler(context);
                return context.ExitCode;
            });
        }

        public static void SetHandler(this Command command, Func<InvocationContext, Task> handler)
        {
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var context = new InvocationContext(parseResult, cancellationToken);
                await handler(context);
                return context.ExitCode;
            });
        }

        public static TValue? GetValueForOption<TValue>(this ParseResult parseResult, Option<TValue> option)
            => parseResult.GetValue(option);

        public static TValue? GetValueForArgument<TValue>(this ParseResult parseResult, Argument<TValue> argument)
            => parseResult.GetValue(argument);

        public static Task<int> InvokeAsync(this Command command, string[] args)
            => command.Parse(args).InvokeAsync();

        public static Task<int> InvokeAsync(this RootCommand command, string[] args)
            => command.Parse(args).InvokeAsync();
    }
}

namespace System.CommandLine.Invocation
{
    internal sealed class InvocationContext
    {
        public InvocationContext(ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            ParseResult = parseResult;
            CancellationToken = cancellationToken;
        }

        public ParseResult ParseResult { get; }

        public CancellationToken CancellationToken { get; }

        public int ExitCode { get; set; }

        public CancellationToken GetCancellationToken() => CancellationToken;
    }
}
