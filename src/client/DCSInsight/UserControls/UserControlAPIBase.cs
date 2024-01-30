﻿using System;
using System.Collections.Generic;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DCSInsight.Events;
using DCSInsight.JSON;
using DCSInsight.Misc;
using NLog;

namespace DCSInsight.UserControls
{
    /// <summary>
    /// Interaction logic for UserControlAPIBase.xaml
    /// </summary>
    public abstract partial class UserControlAPIBase : UserControl, IDisposable, IAsyncDisposable
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected readonly DCSAPI DCSAPI;
        protected bool IsControlLoaded;
        protected readonly List<TextBox> TextBoxParameterList = new();
        protected bool IsConnected;
        private readonly Timer _pollingTimer;
        protected bool CanSend;
        private bool _keepResults;
        protected Button ButtonSend;
        protected Label LabelKeepResults;
        protected CheckBox CheckBoxKeepResults;
        protected Label LabelPolling;
        protected CheckBox CheckBoxPolling;
        protected Label LabelPollingInterval;
        protected ComboBox ComboBoxPollTimes;
        protected Label LabelResultBase;
        protected TextBox TextBoxResultBase;
        private static readonly AutoResetEvent AutoResetEventPolling = new(false);
        protected readonly bool IsLuaConsole;

        public int Id { get; protected set; }
        protected abstract void BuildUI();
        protected abstract void SetFormState();

        protected UserControlAPIBase(DCSAPI dcsAPI, bool isConnected)
        {
            DCSAPI = dcsAPI;
            IsLuaConsole = DCSAPI.Id == Constants.LuaConsole;
            Id = DCSAPI.Id;
            IsConnected = isConnected;
            _pollingTimer = new Timer(PollingTimerCallback);
            _pollingTimer.Change(Timeout.Infinite, 10000);
        }

        public void Dispose()
        {
            AutoResetEventPolling.Set();
            AutoResetEventPolling.Set();
            _pollingTimer?.Dispose();
            AutoResetEventPolling.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_pollingTimer != null)
            {
                AutoResetEventPolling.Set();
                AutoResetEventPolling.Set();
                await _pollingTimer.DisposeAsync();
                AutoResetEventPolling.Dispose();
                GC.SuppressFinalize(this);
            }
        }
        
        protected void SendCommand()
        {
            try
            {
                foreach (var textBox in TextBoxParameterList)
                {
                    var parameterId = (int)textBox.Tag;
                    foreach (var parameter in DCSAPI.Parameters)
                    {
                        if (parameter.Id == parameterId)
                        {
                            parameter.Value = textBox.Text;
                        }
                    }
                }

                ICEventHandler.SendCommand(DCSAPI);
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        private string ResultTextBoxFirstLine()
        {
            var textBoxResultText = "";
            Application.Current.Dispatcher.Invoke(
                DispatcherPriority.Normal,
                (ThreadStart)delegate { textBoxResultText = TextBoxResultBase.Text; });

            if (string.IsNullOrEmpty(textBoxResultText)) return "";

            return textBoxResultText.IndexOf("\n", StringComparison.Ordinal) == -1 ? textBoxResultText : textBoxResultText[..textBoxResultText.IndexOf("\n", StringComparison.Ordinal)];
        }

        internal void SetResult(DCSAPI dcsApi)
        {
            try
            {
                Dispatcher?.BeginInvoke((Action)(() => LabelResultBase.Content = $"Result ({dcsApi.ResultType})"));

                var result = dcsApi.ErrorThrown ? dcsApi.ErrorMessage : string.IsNullOrEmpty(dcsApi.Result) ? "nil" : dcsApi.Result;

                AutoResetEventPolling.Set();


                if (result == ResultTextBoxFirstLine() && result == DCSAPI.Result && !IsLuaConsole)
                {
                    return;
                }

                DCSAPI.Result = result;

                if (_keepResults)
                {
                    Dispatcher?.BeginInvoke((Action)(() => TextBoxResultBase.Text = TextBoxResultBase.Text.Insert(0, result + "\n")));
                    return;
                }
                Dispatcher?.BeginInvoke((Action)(() => TextBoxResultBase.Text = result));
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }


        public void SetConnectionStatus(bool connected)
        {
            try
            {
                IsConnected = connected;
                if (!IsConnected)
                {
                    _pollingTimer.Change(Timeout.Infinite, 10000);
                }
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void ButtonSend_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SendCommand();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        private void StartPolling(int milliseconds)
        {
            try
            {
                _pollingTimer.Change(milliseconds, milliseconds);
                AutoResetEventPolling.Set();
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        private void StopPolling()
        {
            try
            {
                _pollingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void PollingTimerCallback(object state)
        {
            try
            {
                AutoResetEventPolling.WaitOne();
                if (CanSend)
                {
                    Dispatcher?.BeginInvoke((Action)(SendCommand));
                }
            }
            catch (Exception ex)
            {
                ICEventHandler.SendErrorMessage( "Timer Polling Error", ex);
            }
        }
        
        protected void CheckBoxPolling_OnUnchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                StopPolling();
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void CheckBoxPolling_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                StartPolling(int.Parse(ComboBoxPollTimes.SelectedValue.ToString()));
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void ComboBoxPollTimes_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void TextBoxParameter_OnKeyDown_Number(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key is not (>= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or Key.OemPeriod or Key.Tab) && e.Key != Key.OemMinus && e.Key != Key.OemPlus
                    && e.Key != Key.Add && e.Key != Key.Subtract)
                {
                    e.Handled = true;
                    return;
                }
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void TextBoxParameter_OnKeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter && CanSend)
                {
                    SendCommand();
                }
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void CheckBoxKeepResults_OnUnchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _keepResults = false;
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void CheckBoxKeepResults_OnChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _keepResults = true;
                SetFormState();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void TextBoxSyntax_OnMouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Hand;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void TextBoxSyntax_OnMouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Arrow;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        protected void TextBoxSyntax_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var textBox = (TextBox)sender;
                Clipboard.SetText(textBox.Text);
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }
    }
}
