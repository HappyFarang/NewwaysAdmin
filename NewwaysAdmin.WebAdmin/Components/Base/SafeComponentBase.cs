// SafeComponentBase.cs
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Components.Base
{
    public abstract class SafeComponentBase : ComponentBase
    {
        [Inject] protected ILogger<SafeComponentBase> Logger { get; set; } = null!;

        /// <summary>
        /// Safely calls StateHasChanged() with proper error handling for closed circuits
        /// </summary>
        protected async Task SafeStateHasChanged()
        {
            try
            {
                await InvokeAsync(StateHasChanged);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("circuit"))
            {
                Logger?.LogWarning("Cannot update UI - SignalR circuit is closed");
                // Circuit is closed, ignore the error
            }
            catch (ObjectDisposedException)
            {
                Logger?.LogWarning("Cannot update UI - component is disposed");
                // Component is disposed, ignore
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error calling StateHasChanged");
                // Log other errors but don't crash
            }
        }

        /// <summary>
        /// Safely executes an async operation with timeout and circuit handling
        /// </summary>
        protected async Task<T> SafeExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null, T defaultValue = default(T))
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
                return await operation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger?.LogWarning("Operation timed out after {Timeout}", timeout ?? TimeSpan.FromSeconds(30));
                return defaultValue;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("circuit"))
            {
                Logger?.LogWarning("Operation failed - SignalR circuit is closed");
                return defaultValue;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error executing safe operation");
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely executes an async operation without return value
        /// </summary>
        protected async Task SafeExecuteAsync(Func<CancellationToken, Task> operation, TimeSpan? timeout = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
                await operation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger?.LogWarning("Operation timed out after {Timeout}", timeout ?? TimeSpan.FromSeconds(30));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("circuit"))
            {
                Logger?.LogWarning("Operation failed - SignalR circuit is closed");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error executing safe operation");
            }
        }

        /// <summary>
        /// Override OnInitializedAsync with built-in safety
        /// </summary>
        protected virtual async Task SafeOnInitializedAsync()
        {
            // Override this instead of OnInitializedAsync for automatic safety
        }

        protected override async Task OnInitializedAsync()
        {
            await SafeExecuteAsync(async (ct) => await SafeOnInitializedAsync());
        }
    }
}