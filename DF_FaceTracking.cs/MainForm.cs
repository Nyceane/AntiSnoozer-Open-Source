using DF_FaceTracking.cs.Properties;
using face_tracking.cs.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PubNubMessaging.Core;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using voice_synthesis.cs;

namespace DF_FaceTracking.cs
{
    public partial class MainForm : Form
    {
        public enum Label
        {
            StatusLabel,
            AlertsLabel
        };

        public PXCMSession Session;
        public volatile bool Register = false;
        public volatile bool Unregister = false;        
        public volatile bool Stopped = false;

        private readonly object m_bitmapLock = new object();
        private readonly FaceTextOrganizer m_faceTextOrganizer;
        private IEnumerable<CheckBox> m_modulesCheckBoxes;
        private IEnumerable<TextBox> m_modulesTextBoxes; 
        private Bitmap m_bitmap;
        private string m_filename;
        private Tuple<PXCMImage.ImageInfo, PXCMRangeF32> m_selectedColorResolution;
        private volatile bool m_closing;
        private static ToolStripMenuItem m_deviceMenuItem;
        private static ToolStripMenuItem m_moduleMenuItem;
        private static ToolStripMenuItem m_unitMenuItem;
        private static readonly int LANDMARK_ALIGNMENT = -3;

        private System.Timers.Timer yawningTimer;
        private System.Timers.Timer eyeCloseTimer;
        private System.Timers.Timer speakTimer;

        SerialPort currentPort;
        bool portFound;

        static int yawn = 0;
        static int eyeclose = 0;
        static int lookaway = 0;
        static bool stop = false;

        private int edisonValue = 0;

        private bool isYawning = false;
        private bool isEyeClosed = false;
        public MainForm(PXCMSession session)
        {
            InitializeComponent();
            
            m_faceTextOrganizer = new FaceTextOrganizer();
            m_deviceMenuItem = new ToolStripMenuItem("Device");
            m_moduleMenuItem = new ToolStripMenuItem("Module");
            Session = session;
            CreateResolutionMap();
            PopulateDeviceMenu();
            PopulateModuleMenu();
            PopulateProfileMenu();
            PopulateUnitMenu();

            FormClosing += MainForm_FormClosing;
            Panel2.Paint += Panel_Paint;
            yawningTimer = new System.Timers.Timer();
            yawningTimer.Elapsed += new ElapsedEventHandler(OnYawnTimedEvent);
            yawningTimer.Interval = 3000;
            yawningTimer.Enabled = true;

            speakTimer = new System.Timers.Timer();
            speakTimer.Elapsed += new ElapsedEventHandler(OnSpeakEvent);
            speakTimer.Interval = 2500;
            speakTimer.Enabled = true;


            var thread = new Thread(DoTracking);
            thread.Start();


            eyeCloseTimer = new System.Timers.Timer();
            eyeCloseTimer.Elapsed += new ElapsedEventHandler(OnEyeCloseTimedEvent);
            eyeCloseTimer.Interval = 1000;
            eyeCloseTimer.Enabled = true;
            
            currentPort = new SerialPort();
            currentPort.PortName = "COM16"; //Serial port Edison is connected to
            currentPort.BaudRate = 9600;
            currentPort.ReadTimeout = 500;
            currentPort.WriteTimeout = 500;
            try
            {
                currentPort.Open();
            }
            catch(System.IO.IOException ex)
            {
                MessageBox.Show("Edison Unit not detected, please set it to COM16.");
            }
        }

        bool speaking = true;
        private void OnEyeCloseTimedEvent(object source, ElapsedEventArgs e)
        {
            SetText("");
            if (stop)
            {
                yawn = 0;
                eyeclose = 0;
                lookaway = 0;
                edisonValue = 0;
                WriteToSerialPortForced(0);
                setWarningAction(false, true);
                stop = false;
                return;
            }
            DateTime now = DateTime.UtcNow;
            string querystring = String.Empty;

            bool detected = false;
            if (eyeclose > 10)
            {
                querystring += "eyeclose=1";
                Console.WriteLine("Eyeclose: " + eyeclose.ToString());
                SetText("Eye Closed");
                detected = true;
                //                setFlag();
                //                setLight();
            }

            eyeclose = 0;

            if (detected)
            {
                isEyeClosed = true;
                setWarningAction(true);
                speakOutAsync();
            }
            else
            {
                isEyeClosed = false;
                if (!isYawning)
                {
                    setWarningAction(false);
                }
            }
        }

        private void OnSpeakEvent(object source, ElapsedEventArgs e)
        {
            speaking = true;
        }

        private void OnYawnTimedEvent(object source, ElapsedEventArgs e)
        {
            SetText("");
            if (stop)
            {
                yawn = 0;
                eyeclose = 0;
                lookaway = 0;
                edisonValue = 0;
                WriteToSerialPortForced(0);
                setWarningAction(false, true);
                stop = false;
                return;
            }
            DateTime now = DateTime.UtcNow;
            string querystring = String.Empty;

            bool detected = false;

            if (yawn > 10)
            {
                if (!String.IsNullOrEmpty(querystring))
                {
                    querystring += "&";
                }
                querystring += "yawn=1";
                detected = true;

                string tmp = this.StatusLabel.Text;
                if (!String.IsNullOrEmpty(this.StatusLabel.Text))
                {
                    tmp += ", ";
                }
                tmp += "Yawning";
                SetText(tmp);
            }
            
            yawn = 0;

            if (detected)
            {
                isYawning = true;
                setWarningAction(true);
                speakOutAsync();
            }
            else
            {
                isYawning = false;
                if (!isEyeClosed)
                {
                    setWarningAction(false);
                }
            }
        }

        public void clearStatus()
        {
            Console.WriteLine("Clear Status");
            stop = true;
        }

        private void SetText(string text)
        {
//            StatusLabel.Text = text;
        }

        private void WriteToSerialPort(int write)
        {
            Console.WriteLine("edison" + write.ToString());
            if (currentPort.IsOpen)
            {
                if (write == 0)
                {
                    if (!isYawning && !isEyeClosed)
                    {
                        currentPort.Write(write.ToString());
                    }
                }
                else
                {
                    currentPort.Write(write.ToString());
                }
            }
        }

        private void WriteToSerialPortForced(int write)
        {
            currentPort.Write(write.ToString());
            /*
            Console.WriteLine("edison" + write.ToString());
            if (currentPort.IsOpen)
            {
                if (write == 0)
                {
                    if (!isYawning && !isEyeClosed)
                    {
                        currentPort.Write(write.ToString());
                    }
                }
                else
                {
                    currentPort.Write(write.ToString());
                }
            }*/
        }

        bool currentFlag = false;
        #region Push Notifications
        int flagcount = 0;

        public void setWarningAction(bool value)
        {
            if (value)
            {
                Console.WriteLine("flag:" + currentFlag.ToString());
                Console.WriteLine("value:" + value.ToString());
            }
            if (currentFlag != value)
            {
                currentFlag = value;
                if(currentFlag)
                {
                    edisonValue = 1;
                    WriteToSerialPort(edisonValue);
                }
                else
                {
                    edisonValue = 0;
                    WriteToSerialPort(edisonValue);
                }         
            }
                /*
            else
            {
                if (flagcount == 5)
                {
                    setWarningAction(value, true);
                }
                flagcount++;
            }
            */
        }

        public void setWarningAction(bool value, bool enforce)
        {

            currentFlag = value;

        }

        public class Requestor
        {
            public string useruri { get; set; }
            public string priority { get; set; }
            public string sender { get; set; }
            public string subject { get; set; }
            public string displayText { get; set; }
            public string spokenText { get; set; }
        }

        /// <summary>
        /// Callback method captures the response in JSON string format for all operations
        /// </summary>
        /// <param name="result"></param>
        static void DisplayReturnMessage(string result)
        {
            Console.WriteLine("REGULAR CALLBACK:");
            Console.WriteLine(result);
            Console.WriteLine();
        }

        static void DisplayErrorMessageSegments(PubnubClientError pubnubError)
        {
            // These are all the attributes you may be interested in logging, switchiing on etc:

            Console.WriteLine("<STATUS CODE>: {0}", pubnubError.StatusCode); // Unique ID of Error

            Console.WriteLine("<MESSAGE>: {0}", pubnubError.Message); // Message received from server/clent or from .NET exception

            Console.WriteLine("<SEVERITY>: {0}", pubnubError.Severity); // Info can be ignored, Warning and Error should be handled

            if (pubnubError.DetailedDotNetException != null)
            {
                Console.WriteLine(pubnubError.IsDotNetException); // Boolean flag to check .NET exception
                Console.WriteLine("<DETAILED DOT.NET EXCEPTION>: {0}", pubnubError.DetailedDotNetException.ToString()); // Full Details of .NET exception
            }
            Console.WriteLine("<MESSAGE SOURCE>: {0}", pubnubError.MessageSource); // Did this originate from Server or Client-side logic
            if (pubnubError.PubnubWebRequest != null)
            {
                //Captured Web Request details
                Console.WriteLine("<HTTP WEB REQUEST>: {0}", pubnubError.PubnubWebRequest.RequestUri.ToString());
                Console.WriteLine("<HTTP WEB REQUEST - HEADERS>: {0}", pubnubError.PubnubWebRequest.Headers.ToString());
            }
            if (pubnubError.PubnubWebResponse != null)
            {
                //Captured Web Response details
                Console.WriteLine("<HTTP WEB RESPONSE - HEADERS>: {0}", pubnubError.PubnubWebResponse.Headers.ToString());
            }
            Console.WriteLine("<DESCRIPTION>: {0}", pubnubError.Description); // Useful for logging and troubleshooting and support
            Console.WriteLine("<CHANNEL>: {0}", pubnubError.Channel); //Channel name(s) at the time of error
            Console.WriteLine("<DATETIME>: {0}", pubnubError.ErrorDateTimeGMT); //GMT time of error

        }

        private void speakOutAsync()
        {
            try
            {
                if(speaking)
                {
                    speaking = false;
                string sts = VoiceSynthesis.Speak(null,
                    0,
                    "Warning, Drowsy Driving Detected", 100, 100, 100
                    );
                }
//                if (sts != null) MessageBox.Show(sts);
                //Console.WriteLine("spoke");

            }
            catch (Exception e)
            {
                Console.WriteLine("spoke error: " + e.Message);
            }
        }
        #endregion

        public Dictionary<string, PXCMCapture.DeviceInfo> Devices { get; set; }
        public Dictionary<string, IEnumerable<Tuple<PXCMImage.ImageInfo, PXCMRangeF32>>> ColorResolutions { get; set; }
        private readonly List<Tuple<int, int>> SupportedColorResolutions = new List<Tuple<int, int>>
        {
            Tuple.Create(1920, 1080),
            Tuple.Create(1280, 720),
            Tuple.Create(960, 540),
            Tuple.Create(640, 480),
            Tuple.Create(640, 360),
        };
        public string GetFileName()
        {
            return m_filename;
        }

        private void CreateResolutionMap()
        {
            ColorResolutions = new Dictionary<string, IEnumerable<Tuple<PXCMImage.ImageInfo, PXCMRangeF32>>>();
            var desc = new PXCMSession.ImplDesc
            {
                group = PXCMSession.ImplGroup.IMPL_GROUP_SENSOR,
                subgroup = PXCMSession.ImplSubgroup.IMPL_SUBGROUP_VIDEO_CAPTURE
            };

            for (int i = 0; ; i++)
            {
                PXCMSession.ImplDesc desc1;
                if (Session.QueryImpl(desc, i, out desc1) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;

                PXCMCapture capture;
                if (Session.CreateImpl(desc1, out capture) < pxcmStatus.PXCM_STATUS_NO_ERROR) continue;

                for (int j = 0; ; j++)
                {
                    PXCMCapture.DeviceInfo info;
                    if (capture.QueryDeviceInfo(j, out info) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;

                    PXCMCapture.Device device = capture.CreateDevice(j);
                    if (device == null)
                    {
                        throw new Exception("PXCMCapture.Device null");
                    }
                    var deviceResolutions = new List<Tuple<PXCMImage.ImageInfo, PXCMRangeF32>>();

                    for (int k = 0; k < device.QueryStreamProfileSetNum(PXCMCapture.StreamType.STREAM_TYPE_COLOR); k++)
                    {
                        PXCMCapture.Device.StreamProfileSet profileSet;
                        device.QueryStreamProfileSet(PXCMCapture.StreamType.STREAM_TYPE_COLOR, k, out profileSet);
                        PXCMCapture.DeviceInfo dinfo;
                        device.QueryDeviceInfo(out dinfo);
                        var currentRes = new Tuple<PXCMImage.ImageInfo, PXCMRangeF32>(profileSet.color.imageInfo,
                            profileSet.color.frameRate);
                        if (profileSet.color.frameRate.min < 30 || (dinfo != null && dinfo.model == PXCMCapture.DeviceModel.DEVICE_MODEL_DS4 && profileSet.color.imageInfo.width == 1920))
                        {
                            continue;
                        }
                        if (SupportedColorResolutions.Contains(new Tuple<int, int>(currentRes.Item1.width, currentRes.Item1.height)))
                        {
                            deviceResolutions.Add(currentRes);
                        }
                    }
                    ColorResolutions.Add(info.name, deviceResolutions);
                    device.Dispose();
                }                              
                
                capture.Dispose();
            }
        }

        public void PopulateDeviceMenu()
        {
            Devices = new Dictionary<string, PXCMCapture.DeviceInfo>();
            var desc = new PXCMSession.ImplDesc
            {
                group = PXCMSession.ImplGroup.IMPL_GROUP_SENSOR,
                subgroup = PXCMSession.ImplSubgroup.IMPL_SUBGROUP_VIDEO_CAPTURE
            };
                        
            for (int i = 0; ; i++)
            {
                PXCMSession.ImplDesc desc1;
                if (Session.QueryImpl(desc, i, out desc1) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;

                PXCMCapture capture;
                if (Session.CreateImpl(desc1, out capture) < pxcmStatus.PXCM_STATUS_NO_ERROR) continue;

                for (int j = 0; ; j++)
                {
                    PXCMCapture.DeviceInfo dinfo;
                    if (capture.QueryDeviceInfo(j, out dinfo) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                    if (!Devices.ContainsKey(dinfo.name))
                        Devices.Add(dinfo.name, dinfo);

                    var sm1 = new ToolStripMenuItem(dinfo.name, null, Device_Item_Click);
                    m_deviceMenuItem.DropDownItems.Add(sm1);
                }

                capture.Dispose();
            }

            if (m_deviceMenuItem.DropDownItems.Count > 0)
            {
                ((ToolStripMenuItem)m_deviceMenuItem.DropDownItems[0]).Checked = true;
                PopulateColorResolutionMenu(m_deviceMenuItem.DropDownItems[0].ToString());
            }

            try
            {
                MainMenu.Items.RemoveAt(0);
            }
            catch (NotSupportedException)
            {
                m_deviceMenuItem.Dispose();
                throw;
            }
            MainMenu.Items.Insert(0, m_deviceMenuItem);
        }

        public void PopulateUnitMenu()
        {
            var exit = new ToolStripMenuItem("Exit Demo");
            MainMenu.Items.Insert(5, exit);

            exit.Click += (sender, eventArgs) =>
            {
                Application.Exit();
            };
        }

        public void PopulateColorResolutionMenu(string deviceName)
        {
            bool foundDefaultResolution = false;
            var sm = new ToolStripMenuItem("Color");
            foreach (var resolution in ColorResolutions[deviceName])
            {
                var resText = PixelFormat2String(resolution.Item1.format) + " " + resolution.Item1.width + "x"
                                 + resolution.Item1.height + " " + resolution.Item2.max + " fps";
                var sm1 = new ToolStripMenuItem(resText, null);
                var selectedResolution = resolution;
                sm1.Click += (sender, eventArgs) =>
                {
                    m_selectedColorResolution = selectedResolution;
                    ColorResolution_Item_Click(sender);
                };
            
                sm.DropDownItems.Add(sm1);

                if (selectedResolution.Item1.format == PXCMImage.PixelFormat.PIXEL_FORMAT_YUY2 && 
                    selectedResolution.Item1.width == 640 && selectedResolution.Item1.height == 360 && selectedResolution.Item2.min == 30)
                {
                    foundDefaultResolution = true;
                    sm1.Checked = true;
                    sm1.PerformClick();
                }
            }

	        if (!foundDefaultResolution && sm.DropDownItems.Count > 0)
	        {
	            ((ToolStripMenuItem)sm.DropDownItems[0]).Checked = true;
	            ((ToolStripMenuItem)sm.DropDownItems[0]).PerformClick();
	        }

            try
            {
                MainMenu.Items.RemoveAt(1);
            }
            catch (NotSupportedException)
            {
                sm.Dispose();
                throw;
            }
            MainMenu.Items.Insert(1, sm);
        }

        private void PopulateModuleMenu()
        {
            var desc = new PXCMSession.ImplDesc();
            desc.cuids[0] = PXCMFaceModule.CUID;
            
            for (int i = 0; ; i++)
            {
                PXCMSession.ImplDesc desc1;
                if (Session.QueryImpl(desc, i, out desc1) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                var mm1 = new ToolStripMenuItem(desc1.friendlyName, null, Module_Item_Click);
                m_moduleMenuItem.DropDownItems.Add(mm1);
            }
            if (m_moduleMenuItem.DropDownItems.Count > 0)
                ((ToolStripMenuItem)m_moduleMenuItem.DropDownItems[0]).Checked = true;
            try
            {
                MainMenu.Items.RemoveAt(2);
            }
            catch (NotSupportedException)
            {
                m_moduleMenuItem.Dispose();
                throw;
            }
            MainMenu.Items.Insert(2, m_moduleMenuItem);
            
        }

        private void PopulateProfileMenu()
        {
            var pm = new ToolStripMenuItem("Profile");

            foreach (var trackingMode in (PXCMFaceConfiguration.TrackingModeType[])Enum.GetValues(typeof(PXCMFaceConfiguration.TrackingModeType)))
            {
                var pm1 = new ToolStripMenuItem(FaceMode2String(trackingMode), null, Profile_Item_Click);
                pm.DropDownItems.Add(pm1);

                if (trackingMode == PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH) //3d = default
                {
                    pm1.Checked = true;
                }
            }
            try
            {
                MainMenu.Items.RemoveAt(3);
            }
            catch (NotSupportedException)
            {
                pm.Dispose();
                throw;
            }
            MainMenu.Items.Insert(3, pm);
        }

        private static string FaceMode2String(PXCMFaceConfiguration.TrackingModeType mode)
        {
            switch (mode)
            {
                case PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR:
                    return "2D Tracking";
                case PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH:
                    return "3D Tracking";
            }
            return "";
        }

        private static string PixelFormat2String(PXCMImage.PixelFormat format)
        {
            switch (format)
            {
                case PXCMImage.PixelFormat.PIXEL_FORMAT_YUY2:
                    return "YUY2";
                case PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32:
                    return "RGB32";
                case PXCMImage.PixelFormat.PIXEL_FORMAT_RGB24:
                    return "RGB24";                
            }
            return "NA";
        }

        private void RadioCheck(object sender, string name)
        {
            foreach (ToolStripMenuItem m in MainMenu.Items)
            {
                if (!m.Text.Equals(name)) continue;
                foreach (ToolStripMenuItem e1 in m.DropDownItems)
                {
                    e1.Checked = (sender == e1);
                }
            }
        }

        private void ColorResolution_Item_Click(object sender)
        {
            RadioCheck(sender, "Color");
        }

        private void Device_Item_Click(object sender, EventArgs e)
        {
            PopulateColorResolutionMenu(sender.ToString());
            RadioCheck(sender, "Device");
        }

        private void Module_Item_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Module");
            PopulateProfileMenu();
        }

        private void Profile_Item_Click(object sender, EventArgs e)
        {
            RadioCheck(sender, "Profile");
        }

        private void DoTracking()
        {
            var ft = new FaceTracking(this);
            ft.SimplePipeline();
        }

        public string GetCheckedDevice()
        {
            return (from ToolStripMenuItem m in MainMenu.Items
                where m.Text.Equals("Device")
                from ToolStripMenuItem e in m.DropDownItems
                where e.Checked
                select e.Text).FirstOrDefault();
        }

        public Tuple<PXCMImage.ImageInfo, PXCMRangeF32> GetCheckedColorResolution()
        {
            return m_selectedColorResolution;
        }

        public string GetCheckedModule()
        {
            return (from ToolStripMenuItem m in MainMenu.Items
                where m.Text.Equals("Module")
                from ToolStripMenuItem e in m.DropDownItems
                where e.Checked
                select e.Text).FirstOrDefault();
        }

        public string GetCheckedProfile()
        {
            foreach (var m in from ToolStripMenuItem m in MainMenu.Items where m.Text.Equals("Profile") select m)
            {
                for (var i = 0; i < m.DropDownItems.Count; i++)
                {
                    if (((ToolStripMenuItem) m.DropDownItems[i]).Checked)
                        return m.DropDownItems[i].Text;
                }
            }
            return "";
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            /*
            Stopped = true;
            e.Cancel = Stop.Enabled;
            m_closing = true;
             */
            Console.WriteLine("Closing");
            if (currentPort != null && currentPort.IsOpen)
            {
                currentPort.Close();
            }
            Stopped = true;
            m_closing = true;
        }

        public void UpdateStatus(string status, Label label)
        {
            try
            {
                if (m_closing)
                {
                    return;
                }
                if (label == Label.StatusLabel)
                    Status2.Invoke(new UpdateStatusDelegate(delegate(string s) { StatusLabel.Text = s; }),
                        new object[] { status });
                /*
                if (label == Label.AlertsLabel)
                    Status2.Invoke(new UpdateStatusDelegate(delegate(string s) { AlertsLabel.Text = s; }),
                        new object[] { status });*/
            }
            catch (ObjectDisposedException e)
            {
                Console.WriteLine(e.Message);
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private void Panel_Paint(object sender, PaintEventArgs e)
        {
            lock (m_bitmapLock)
            {
                if (m_bitmap == null) return;

                e.Graphics.DrawImage(m_bitmap, Panel2.ClientRectangle);
            }
        }

        public void UpdatePanel()
        {
           
            try
            {
                if (m_closing)
                {
                    return;
                }
                Panel2.Invoke(new UpdatePanelDelegate(() => Panel2.Invalidate()));
            }
            catch (ObjectDisposedException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public void DrawBitmap(Bitmap picture)
        {
            lock (m_bitmapLock)
            {
                if (m_bitmap != null)
                {
                    m_bitmap.Dispose();
                }
                m_bitmap = new Bitmap(picture);
            }
        }

        public void DrawGraphics(PXCMFaceData moduleOutput)
        {
            Debug.Assert(moduleOutput != null);

            for (var i = 0; i < moduleOutput.QueryNumberOfDetectedFaces(); i++)
            {
                PXCMFaceData.Face face = moduleOutput.QueryFaceByIndex(i);
                if (face == null)
                {
                    throw new Exception("DrawGraphics::PXCMFaceData.Face null");
                }
                
                lock (m_bitmapLock)
                {
                    m_faceTextOrganizer.ChangeFace(i, face, m_bitmap.Height, m_bitmap.Width);
                }

                DrawLocation(face);
                DrawLandmark(face);
                DrawPose(face);
                DrawPulse(face);
                DrawExpressions(face);
                DrawRecognition(face);
            }
        }

        #region Playback / Record

        private void Live_Click(object sender, EventArgs e)
        {
            Playback.Checked = Record.Checked = false;
            Live.Checked = true;
        }

        private void Playback_Click(object sender, EventArgs e)
        {
            Live.Checked = Record.Checked = false;
            Playback.Checked = true;
            var ofd = new OpenFileDialog
            {
                Filter = @"RSSDK clip|*.rssdk|Old format clip|*.pcsdk|All files|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };
            try
            {
                m_filename = (ofd.ShowDialog() == DialogResult.OK) ? ofd.FileName : null;                
            }
            catch (Exception)
            {
                ofd.Dispose();
                throw;
            }
            ofd.Dispose();
        }

        public bool GetPlaybackState()
        {
            return Playback.Checked;
        }

        private void Record_Click(object sender, EventArgs e)
        {
            Live.Checked = Playback.Checked = false;
            Record.Checked = true;
            var sfd = new SaveFileDialog
            {
                Filter = @"RSSDK clip|*.rssdk|All files|*.*",
                CheckPathExists = true,
                OverwritePrompt = true,
                AddExtension    = true
            };
            try
            {
                m_filename = (sfd.ShowDialog() == DialogResult.OK) ? sfd.FileName : null;
            }
            catch (Exception)
            {
                sfd.Dispose();
                throw;
            }
            sfd.Dispose();
        }

        public bool GetRecordState()
        {
            return Record.Checked;
        }

        public string GetPlaybackFile()
        {
            return Invoke(new GetFileDelegate(() =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = @"All files (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };
                return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : null;
            })) as string;
        }

        public string GetRecordFile()
        {
            return Invoke(new GetFileDelegate(() =>
            {
                var sfd = new SaveFileDialog
                {
                    Filter = @"All files (*.*)|*.*",
                    CheckFileExists = true,
                    OverwritePrompt = true
                };
                if (sfd.ShowDialog() == DialogResult.OK) return sfd.FileName;
                return null;
            })) as string;
        }

        private delegate string GetFileDelegate();

        #endregion

        #region Modules Drawing

        private static readonly Assembly m_assembly = Assembly.GetExecutingAssembly();

        private readonly ResourceSet m_resources = 
            new ResourceSet(m_assembly.GetManifestResourceStream(@"DF_FaceTracking.cs.Properties.Resources.resources"));

        private readonly Dictionary<PXCMFaceData.ExpressionsData.FaceExpression, Bitmap> m_cachedExpressions =
            new Dictionary<PXCMFaceData.ExpressionsData.FaceExpression, Bitmap>();

        private readonly Dictionary<PXCMFaceData.ExpressionsData.FaceExpression, string> m_expressionDictionary =
            new Dictionary<PXCMFaceData.ExpressionsData.FaceExpression, string>
            {
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_MOUTH_OPEN, @"MouthOpen"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_SMILE, @"Smile"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_KISS, @"Kiss"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_UP, @"Eyes_Turn_Up"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_DOWN, @"Eyes_Turn_Down"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_TURN_LEFT, @"Eyes_Turn_Left"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_TURN_RIGHT, @"Eyes_Turn_Right"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_CLOSED_LEFT, @"Eyes_Closed_Left"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_CLOSED_RIGHT, @"Eyes_Closed_Right"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_LOWERER_RIGHT, @"Brow_Lowerer_Right"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_LOWERER_LEFT, @"Brow_Lowerer_Left"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_RAISER_RIGHT, @"Brow_Raiser_Right"},
                {PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_RAISER_LEFT, @"Brow_Raiser_Left"}
            };

        public void DrawLocation(PXCMFaceData.Face face)
        {
            return;
            Debug.Assert(face != null);
            if (m_bitmap == null) return;

            PXCMFaceData.DetectionData detection = face.QueryDetection();
            if (detection == null)
                return;

            lock (m_bitmapLock)
            {
                using (Graphics graphics = Graphics.FromImage(m_bitmap))
                using (var pen = new Pen(m_faceTextOrganizer.Colour, 3.0f))
                using (var brush = new SolidBrush(m_faceTextOrganizer.Colour))
                using (var font = new Font(FontFamily.GenericMonospace, m_faceTextOrganizer.FontSize, FontStyle.Bold))
                {
                    graphics.DrawRectangle(pen, m_faceTextOrganizer.RectangleLocation);
                    String faceId = String.Format("Face ID: {0}",
                        face.QueryUserID().ToString(CultureInfo.InvariantCulture));
                    graphics.DrawString(faceId, font, brush, m_faceTextOrganizer.FaceIdLocation);
                }
            }
        }
            
        public void DrawLandmark(PXCMFaceData.Face face)
        {
            Debug.Assert(face != null);
            PXCMFaceData.LandmarksData landmarks = face.QueryLandmarks();
            if (m_bitmap == null || landmarks == null) return;

            lock (m_bitmapLock)
            {
                using (Graphics graphics = Graphics.FromImage(m_bitmap))
                using (var brush = new SolidBrush(Color.White))
                using (var lowConfidenceBrush = new SolidBrush(Color.Red))
                using (var font = new Font(FontFamily.GenericMonospace, m_faceTextOrganizer.FontSize, FontStyle.Bold))
                {
                    PXCMFaceData.LandmarkPoint[] points;
                    bool res = landmarks.QueryPoints(out points);
                    Debug.Assert(res);

                    var point = new PointF();

                    foreach (PXCMFaceData.LandmarkPoint landmark in points)
                    {
                        point.X = landmark.image.x + LANDMARK_ALIGNMENT;
                        point.Y = landmark.image.y + LANDMARK_ALIGNMENT;

                        if (landmark.confidenceImage == 0)
                            graphics.DrawString("x", font, lowConfidenceBrush, point);
                        else
                            graphics.DrawString("•", font, brush, point);
                    }
                }
            }
        }

        public void DrawPose(PXCMFaceData.Face face)
        {
            return;
            Debug.Assert(face != null);
            PXCMFaceData.PoseEulerAngles poseAngles;
            PXCMFaceData.PoseData pdata = face.QueryPose();
            if (pdata == null)
            {
                return;
            }
            if (!pdata.QueryPoseAngles(out poseAngles)) return;

            lock (m_bitmapLock)
            {
                using (Graphics graphics = Graphics.FromImage(m_bitmap))
                using (var brush = new SolidBrush(m_faceTextOrganizer.Colour))
                using (var font = new Font(FontFamily.GenericMonospace, m_faceTextOrganizer.FontSize, FontStyle.Bold))
                {
                    string yawText = String.Format("Yaw = {0}",
                        Convert.ToInt32(poseAngles.yaw).ToString(CultureInfo.InvariantCulture));
                    graphics.DrawString(yawText, font, brush, m_faceTextOrganizer.PoseLocation.X,
                        m_faceTextOrganizer.PoseLocation.Y);

                    string pitchText = String.Format("Pitch = {0}",
                        Convert.ToInt32(poseAngles.pitch).ToString(CultureInfo.InvariantCulture));
                    graphics.DrawString(pitchText, font, brush, m_faceTextOrganizer.PoseLocation.X,
                        m_faceTextOrganizer.PoseLocation.Y + m_faceTextOrganizer.FontSize);

                    string rollText = String.Format("Roll = {0}",
                        Convert.ToInt32(poseAngles.roll).ToString(CultureInfo.InvariantCulture));
                    graphics.DrawString(rollText, font, brush, m_faceTextOrganizer.PoseLocation.X,
                        m_faceTextOrganizer.PoseLocation.Y + 2 * m_faceTextOrganizer.FontSize);
                }
            }
        }

        public void DrawExpressions(PXCMFaceData.Face face)
        {
            Debug.Assert(face != null);
            if (m_bitmap == null) return;

            PXCMFaceData.ExpressionsData expressionsOutput = face.QueryExpressions();

            if (expressionsOutput == null) return;

            lock (m_bitmapLock)
            {
                using (Graphics graphics = Graphics.FromImage(m_bitmap))
                using (var brush = new SolidBrush(m_faceTextOrganizer.Colour))
                {
                    const int imageSizeWidth = 18;
                    const int imageSizeHeight = 18;

                    int positionX = m_faceTextOrganizer.ExpressionsLocation.X;
                    int positionXText = positionX + imageSizeWidth;
                    int positionY = m_faceTextOrganizer.ExpressionsLocation.Y;
                    int positionYText = positionY + imageSizeHeight / 4;

                    foreach (var expressionEntry in m_expressionDictionary)
                    {
                        PXCMFaceData.ExpressionsData.FaceExpression expression = expressionEntry.Key;
                        PXCMFaceData.ExpressionsData.FaceExpressionResult result;
                        bool status = expressionsOutput.QueryExpression(expression, out result);
                        if (!status) continue;

                        
                        if (expressionEntry.Value.Equals(@"MouthOpen"))
                        {
                            if (result.intensity > 50)
                            {
                                yawn++;
                            }
                        }

                        if (expressionEntry.Value.Equals(@"Eyes_Closed_Left") || expressionEntry.Value.Equals(@"Eyes_Closed_Right"))
                        {
                            if (result.intensity > 50)
                            {
                                eyeclose++;
                            }
                        }

                        if (expressionEntry.Value.Equals(@"Eyes_Turn_Right") || expressionEntry.Value.Equals(@"Eyes_Turn_Left"))
                        {
                            if (result.intensity > 20)
                            {
                                lookaway++;
                            }
                        }
                        /*
                        Bitmap cachedExpressionBitmap;
                        bool hasCachedExpressionBitmap = m_cachedExpressions.TryGetValue(expression, out cachedExpressionBitmap);
                        if (!hasCachedExpressionBitmap)
                        {
                            cachedExpressionBitmap = (Bitmap) m_resources.GetObject(expressionEntry.Value);
                            m_cachedExpressions.Add(expression, cachedExpressionBitmap);
                        }

                        using (var font = new Font(FontFamily.GenericMonospace, m_faceTextOrganizer.FontSize, FontStyle.Bold))
                        {
                            Debug.Assert(cachedExpressionBitmap != null, "cachedExpressionBitmap != null");
                            graphics.DrawImage(cachedExpressionBitmap, new Rectangle(positionX, positionY, imageSizeWidth, imageSizeHeight));
                            var expressionText = String.Format("= {0}", result.intensity);
                            graphics.DrawString(expressionText, font, brush, positionXText, positionYText);

                            positionY += imageSizeHeight;
                            positionYText += imageSizeHeight;
                        }*/
                    }
                }
            }
        }

        public void DrawRecognition(PXCMFaceData.Face face)
        {
            return;
        }
        
        private void DrawPulse(PXCMFaceData.Face face)
        {
            return;
            /*
            Debug.Assert(face != null);
            if (m_bitmap == null) return;

            var pulseData = face.QueryPulse();
            if (pulseData == null)            
                return;               

            lock (m_bitmapLock)
            {
                var pulseString = "Pulse: " + pulseData.QueryHeartRate();
                
                using (var graphics = Graphics.FromImage(m_bitmap))
                using (var brush = new SolidBrush(m_faceTextOrganizer.Colour))
                using (var font = new Font(FontFamily.GenericMonospace, m_faceTextOrganizer.FontSize, FontStyle.Bold))
                {
                    graphics.DrawString(pulseString, font, brush, m_faceTextOrganizer.PulseLocation);
                }
            }
            */
        }

        #endregion

        private delegate void DoTrackingCompleted();

        private delegate void UpdatePanelDelegate();

        private delegate void UpdateStatusDelegate(string status);


        #region gesture

        public void DisplayJoints(PXCMHandData.JointData[][] nodes, int numOfHands)
        {
            if (m_bitmap == null) return;
            if (nodes == null) return;


                lock (this)
                {
                    try
                    {
                        int scaleFactor = 1;
                        Graphics g = Graphics.FromImage(m_bitmap);

                        using (Pen boneColor = new Pen(Color.DodgerBlue, 3.0f))
                        {
                            for (int i = 0; i < numOfHands; i++)
                            {
                                if (nodes[i][0] == null) continue;
                                int baseX = (int)nodes[i][0].positionImage.x / scaleFactor;
                                int baseY = (int)nodes[i][0].positionImage.y / scaleFactor;

                                int wristX = (int)nodes[i][0].positionImage.x / scaleFactor;
                                int wristY = (int)nodes[i][0].positionImage.y / scaleFactor;


                                for (int j = 1; j < 22; j++)
                                {
                                    if (nodes[i][j] == null) continue;
                                    int x = (int)nodes[i][j].positionImage.x / scaleFactor;
                                    int y = (int)nodes[i][j].positionImage.y / scaleFactor;

                                    if (nodes[i][j].confidence <= 0) continue;

                                    if (j == 2 || j == 6 || j == 10 || j == 14 || j == 18)
                                    {

                                        baseX = wristX;
                                        baseY = wristY;
                                    }

                                    g.DrawLine(boneColor, new Point(baseX, baseY), new Point(x, y));
                                    baseX = x;
                                    baseY = y;
                                }



                                using (
                                    Pen red = new Pen(Color.Red, 3.0f),
                                        black = new Pen(Color.Black, 3.0f),
                                        green = new Pen(Color.Green, 3.0f),
                                        blue = new Pen(Color.Blue, 3.0f),
                                        cyan = new Pen(Color.Cyan, 3.0f),
                                        yellow = new Pen(Color.Yellow, 3.0f),
                                        orange = new Pen(Color.Orange, 3.0f))
                                {
                                    Pen currnetPen = black;

                                    for (int j = 0; j < PXCMHandData.NUMBER_OF_JOINTS; j++)
                                    {
                                        float sz = 4;
                                        //if (Labelmap.Checked)
                                        sz = 2;

                                        int x = (int)nodes[i][j].positionImage.x / scaleFactor;
                                        int y = (int)nodes[i][j].positionImage.y / scaleFactor;

                                        if (nodes[i][j].confidence <= 0) continue;

                                        //Wrist
                                        if (j == 0)
                                        {
                                            currnetPen = black;
                                        }

                                        //Center
                                        if (j == 1)
                                        {
                                            currnetPen = red;
                                            sz += 4;
                                        }

                                        //Thumb
                                        if (j == 2 || j == 3 || j == 4 || j == 5)
                                        {
                                            currnetPen = green;
                                        }
                                        //Index Finger
                                        if (j == 6 || j == 7 || j == 8 || j == 9)
                                        {
                                            currnetPen = blue;
                                        }
                                        //Finger
                                        if (j == 10 || j == 11 || j == 12 || j == 13)
                                        {
                                            currnetPen = yellow;
                                        }
                                        //Ring Finger
                                        if (j == 14 || j == 15 || j == 16 || j == 17)
                                        {
                                            currnetPen = cyan;
                                        }
                                        //Pinkey
                                        if (j == 18 || j == 19 || j == 20 || j == 21)
                                        {
                                            currnetPen = orange;
                                        }


                                        if (j == 5 || j == 9 || j == 13 || j == 17 || j == 21)
                                        {
                                            sz += 4;
                                            //currnetPen.Width = 1;
                                        }

                                        g.DrawEllipse(currnetPen, x - sz / 2, y - sz / 2, sz, sz);
                                    }

                                }



                            }

                        }
                        g.Dispose();
                    }
                    catch(InvalidOperationException ex)
                    {
                        return;
                    }
                }
            
        }

        #endregion

        #region
        public bool CheckForInternetConnection()
        {
            try
            {
                using (var client = new WebClient())
                using (var stream = client.OpenRead("http://www.google.com"))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        #endregion
    }
}
