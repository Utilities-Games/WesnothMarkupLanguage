using WesnothMarkupLanguage.CampaignValidator;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
return await CampaignValidatorApplication.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
