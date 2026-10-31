using System.Windows;
using System.Windows.Controls;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //view for inspecting editing or retrying a single voice note
    public partial class VoiceNoteDetailView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly VoiceNote _note;
        private readonly Action _back;

        public VoiceNoteDetailView(WidgetFlow flow, VoiceNote note, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _note = note;
            _back = back;

            WindowText.Text = Format.Range(note.WindowStart, note.WindowEnd);
            StatusText.Text = $"Status: {note.Status}";
            TranscriptBox.Text = note.Transcript ?? string.Empty;

            //display failure details when applicable
            if (note.Status == VoiceNoteStatus.Failed)
            {
                ErrorText.Text = note.Error ?? "Could not transcribe audio.";
                ErrorText.Visibility = Visibility.Visible;
                RetryButton.Visibility = Visibility.Visible;
            }
        }

        //-----------------------------
        //saves updated transcript text
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            SaveButton.Content = "Saving…";
            ActionErrorText.Text = string.Empty;
            ActionErrorText.Visibility = Visibility.Collapsed;

            try
            {
                await _flow.Ai.UpdateTranscriptAsync(_note.Id, TranscriptBox.Text);
                SaveButton.Content = "Saved ✓";
            }
            catch (Exception ex)
            {
                ActionErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't save the transcript. Try again." : ex.Message;
                ActionErrorText.Visibility = Visibility.Visible;
                SaveButton.Content = "Save";
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //re-enqueues failed voice note for processing
        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            RetryButton.IsEnabled = false;
            ActionErrorText.Text = string.Empty;
            ActionErrorText.Visibility = Visibility.Collapsed;

            try
            {
                await _flow.Ai.RetryNoteAsync(_note.Id);
                StatusText.Text = "Status: Recorded";
                ErrorText.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't retry transcription. Try again." : ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                RetryButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //shows inline confirmation for deletion
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DeleteButton.Visibility = Visibility.Collapsed;
            ConfirmDeletePanel.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //aborts note deletion
        private void CancelDelete_Click(object sender, RoutedEventArgs e)
        {
            ConfirmDeletePanel.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //confirms note deletion and navigates back
        private async void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }
            ActionErrorText.Text = string.Empty;
            ActionErrorText.Visibility = Visibility.Collapsed;

            try
            {
                await _flow.Ai.DeleteNoteAsync(_note.Id);
                _back();
            }
            catch (Exception ex)
            {
                ActionErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't delete the note. Try again." : ex.Message;
                ActionErrorText.Visibility = Visibility.Visible;
                if (btn != null)
                {
                    btn.IsEnabled = true;
                }
            }
        }

        //-----------------------------
        //returns to previous screen
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _back();
        }
    }
}
//------------------------------EOF-----------------------------\\
