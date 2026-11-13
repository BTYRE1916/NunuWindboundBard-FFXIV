using WindboundCaptureEngine.Core;

namespace WindboundCaptureEngine;

public sealed class MainForm : Form
{
    private readonly CaptureController controller = new();
    private readonly PipeServer pipe;
    private readonly Label status = new();
    private readonly TextBox output = new();
    private readonly NumericUpDown width = new() { Minimum = 640, Maximum = 7680, Value = 3840, Increment = 160 };
    private readonly NumericUpDown height = new() { Minimum = 480, Maximum = 4320, Value = 2160, Increment = 120 };
    private readonly NumericUpDown fps = new() { Minimum = 24, Maximum = 120, Value = 60 };
    private readonly NumericUpDown bitrate = new() { Minimum = 5, Maximum = 150, Value = 35 };
    private readonly ComboBox encoder = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };

    public MainForm()
    {
        pipe = new PipeServer(controller); pipe.Start();
        Text = "Windbound Concert Studio — Capture Engine"; Width = 760; Height = 510; MinimumSize = new Size(680, 450);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(20, 22, 30); ForeColor = Color.Gainsboro;
        BuildUi(); timer.Tick += (_, _) => RefreshStatus(); timer.Start();
        FormClosing += (_, _) => { timer.Stop(); pipe.Dispose(); controller.Dispose(); };
    }

    private void BuildUi()
    {
        Controls.Add(new Label { Text = "WINDBOUND CONCERT STUDIO", Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 112, 165), AutoSize = true, Location = new Point(24, 20) });
        Controls.Add(new Label { Text = "Isolated native capture engine • independent of FFXIV / Dalamud", AutoSize = true,
            Location = new Point(27, 58), ForeColor = Color.Silver });
        LabelAt("Recording file (MKV)", 25, 100);
        output.SetBounds(25, 124, 555, 30); output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WindboundConcert.mkv"); Controls.Add(output);
        var browse = new Button { Text = "Browse…", Bounds = new Rectangle(595, 123, 115, 32) };
        browse.Click += (_, _) => { using var d = new SaveFileDialog { Filter = "Matroska video|*.mkv", FileName = Path.GetFileName(output.Text) }; if (d.ShowDialog(this) == DialogResult.OK) output.Text = d.FileName; };
        Controls.Add(browse);
        LabelAt("Resolution", 25, 175); width.SetBounds(25, 198, 120, 30); Controls.Add(width); height.SetBounds(155, 198, 120, 30); Controls.Add(height);
        LabelAt("FPS", 295, 175); fps.SetBounds(295, 198, 80, 30); Controls.Add(fps);
        LabelAt("Bitrate Mbps", 395, 175); bitrate.SetBounds(395, 198, 100, 30); Controls.Add(bitrate);
        LabelAt("Encoder", 515, 175); encoder.Items.AddRange(["h264_nvenc", "hevc_nvenc", "av1_nvenc", "libx264"]); encoder.SelectedIndex = 0; encoder.SetBounds(515, 198, 195, 30); Controls.Add(encoder);
        var start = new Button { Text = "START RECORDING", Bounds = new Rectangle(25, 250, 210, 42), BackColor = Color.FromArgb(145, 44, 82), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        start.Click += async (_, _) => { var r = await controller.HandleAsync(new EngineRequest("start", output.Text, (int)width.Value, (int)height.Value, (int)fps.Value, encoder.SelectedItem?.ToString() ?? "h264_nvenc", (int)bitrate.Value)); status.Text = r.Message; };
        Controls.Add(start);
        var stop = new Button { Text = "STOP & FINALISE", Bounds = new Rectangle(250, 250, 190, 42), BackColor = Color.FromArgb(65, 67, 78), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        stop.Click += async (_, _) => { var r = await controller.HandleAsync(new EngineRequest("stop")); status.Text = r.Message; }; Controls.Add(stop);
        status.SetBounds(25, 310, 690, 55); status.ForeColor = Color.FromArgb(255, 190, 110); Controls.Add(status);
        Controls.Add(new Label { Bounds = new Rectangle(25, 375, 690, 85), ForeColor = Color.Silver,
            Text = "Milestone 1: desktop/output capture through Windows Desktop Duplication (FFmpeg ddagrab), NVENC or x264, and MKV output. " +
            "This is not OBS and runs outside the game process. Window-target capture, audio mixer, scene compositor, streaming, replay buffer, " +
            "plugin UI integration and reliable pause/resume are follow-on milestones. Pause/resume is not faked." });
    }
    private void LabelAt(string text, int x, int y) => Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.Gainsboro });
    private void RefreshStatus() { var s = controller.Snapshot; status.Text = $"{s.State} • {s.Message}" + (s.ElapsedSeconds > 0 ? $" • {TimeSpan.FromSeconds(s.ElapsedSeconds):hh\\:mm\\:ss}" : ""); }
}
