using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Raccoonwatch.Infrastructure;

namespace Raccoonwatch.UI;

/// <summary>About dialog showing the product name, tagline, artwork and GitHub link.</summary>
public sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/mootoneu/raccoonwatch.git";
    private const string ArtworkResource = "Raccoonwatch.assets.raccoonwatch_icon_1254x1254.png";
    private const string GitHubIconResource = "Raccoonwatch.assets.github-mark.png";

    public AboutForm()
    {
        Text = "About Raccoon Watch";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 640);
        BackColor = Color.White;

        BuildUi();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20, 16, 20, 16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 400));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(new Label
        {
            Text = "Raccoon Watch",
            Font = new Font(FontFamily.GenericSansSerif, 22F, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "Manage your processes easily",
            Font = new Font(FontFamily.GenericSansSerif, 11F),
            ForeColor = Color.Gray,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter
        }, 0, 1);

        var artwork = new PictureBox
        {
            Image = LoadEmbeddedImage(ArtworkResource, 400),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(400, 400),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        layout.Controls.Add(artwork, 0, 2);

        var linkPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            WrapContents = false
        };
        var githubIcon = new PictureBox
        {
            Image = LoadEmbeddedImage(GitHubIconResource, 28),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(28, 28),
            Margin = new Padding(0, 2, 8, 0)
        };
        var link = new LinkLabel
        {
            Text = RepositoryUrl,
            LinkArea = new LinkArea(0, RepositoryUrl.Length),
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 10F),
            Padding = new Padding(0, 6, 0, 0)
        };
        link.LinkClicked += (_, _) => OpenRepository();
        linkPanel.Controls.Add(githubIcon);
        linkPanel.Controls.Add(link);
        layout.Controls.Add(linkPanel, 0, 3);

        var close = new Button
        {
            Text = "Close",
            AccessibleName = "Close",
            Size = new Size(90, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        close.Click += (_, _) => Hide();
        layout.Controls.Add(close, 0, 4);

        Controls.Add(layout);
    }

    private static Image LoadEmbeddedImage(string resourceName, int width)
    {
        using var stream = typeof(AboutForm).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        using var source = Image.FromStream(stream);

        var height = (int)Math.Round(source.Height * (width / (double)source.Width));
        var scaled = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);
        return scaled;
    }

    private static void OpenRepository()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(RepositoryUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the link: {ex.Message}",
                "Raccoon Watch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
