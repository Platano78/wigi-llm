using System;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

namespace ClipboardAgentWidget
{
    public static class ClipboardManager
    {
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 100;

        public static string GetText()
        {
            string result = null;
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    Log("Attempt " + attempt + " to get clipboard text");
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            if (Clipboard.ContainsText())
                            {
                                result = Clipboard.GetText();
                            }
                            else
                            {
                                result = string.Empty;
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(1000);

                    if (result != null)
                    {
                        Log("GetText succeeded on attempt " + attempt);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Log("GetText failed on attempt " + attempt + ": " + ex.Message);
                }

                if (attempt < MaxRetries)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }

            Log("GetText failed after " + MaxRetries + " attempts");
            return string.Empty;
        }

        public static bool SetText(string text)
        {
            if (text == null) text = string.Empty;
            bool success = false;
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    Log("Attempt " + attempt + " to set clipboard text");
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            Clipboard.SetText(text);
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(1000);

                    if (success)
                    {
                        Log("SetText succeeded on attempt " + attempt);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Log("SetText failed on attempt " + attempt + ": " + ex.Message);
                }

                if (attempt < MaxRetries)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }

            Log("SetText failed after " + MaxRetries + " attempts");
            return false;
        }

        public static bool IsClipboardAvailable()
        {
            try
            {
                bool available = false;
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        Clipboard.ContainsText();
                        available = true;
                    }
                    catch { }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join(500);
                return available;
            }
            catch
            {
                return false;
            }
        }

        private static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine("[ClipboardAgent] " + message);
        }
    }
}