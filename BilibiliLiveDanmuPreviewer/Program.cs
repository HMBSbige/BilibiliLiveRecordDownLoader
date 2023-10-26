using IAbpApplicationWithInternalServiceProvider application = await AbpApplicationFactory.CreateAsync<BilibiliLiveDanmuPreviewerModule>(options => options.UseAutofac());

try
{
	await application.InitializeAsync();
	MainService service = application.ServiceProvider.GetRequiredService<MainService>();
	try
	{
		using CancellationTokenSource cts = new();
		using (Observable.FromEventPattern<ConsoleCancelEventArgs>(typeof(Console), nameof(Console.CancelKeyPress)).Subscribe(e =>
		{
			// ReSharper disable once AccessToDisposedClosure
			cts.Cancel();
			e.EventArgs.Cancel = true;
		}))
		{
			await service.DoAsync(cts.Token);
		}
	}
	catch (Exception ex)
	{
		application.ServiceProvider.GetRequiredService<ILogger<MainService>>().LogException(ex);
		return 1;
	}

	return 0;
}
finally
{
	await application.ShutdownAsync();
}
