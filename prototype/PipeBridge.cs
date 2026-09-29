using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace WhaleAlive;
// Same-user named pipe: no TCP port, browser origin, credential file, or remote listener.
public sealed class PipeBridge : IDisposable
{
    readonly CancellationTokenSource stop = new(); readonly Func<JsonElement, Task<object>> handler;
    public PipeBridge(Func<JsonElement, Task<object>> handler) { this.handler = handler; _ = Listen(); }
    async Task Listen() { while (!stop.IsCancellationRequested) { NamedPipeServerStream? pipe = null; try { pipe = new("WhaleAlive.Prototype", PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await pipe.WaitForConnectionAsync(stop.Token); _ = Serve(pipe); pipe = null; } catch (OperationCanceledException) { break; } catch { await Task.Delay(500); } finally { pipe?.Dispose(); } } }
    async Task Serve(NamedPipeServerStream pipe) { using (pipe) { try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromMinutes(11)); var bytes = new byte[8192]; int count = 0; while (count < bytes.Length) { int n = await pipe.ReadAsync(bytes.AsMemory(count, 1), timeout.Token); if (n == 0) return; if (bytes[count++] == 10) break; } if (count == 8192) throw new ArgumentException("命令太长"); using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 0, count)); object result; try { var pending = await Application.Current.Dispatcher.InvokeAsync(() => handler(doc.RootElement)); result = new { ok = true, result = await pending }; } catch (Exception e) { result = new { ok = false, error = e.Message }; } var response = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result) + "\n"); await pipe.WriteAsync(response, timeout.Token); } catch (OperationCanceledException) { } catch (IOException) { } catch (JsonException) { } } }
    public void Dispose() { stop.Cancel(); }
}


