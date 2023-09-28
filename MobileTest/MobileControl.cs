using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VideoOS.Mobile.Portable.MetaChannel;
using VideoOS.Mobile.Portable.Utilities;
using VideoOS.Mobile.Portable.VideoChannel.Binary;
using VideoOS.Mobile.Portable.VideoChannel.Params;
using VideoOS.Mobile.Portable.ViewGroupItem;
using VideoOS.Mobile.SDK.Portable.Server.Base.CommandResults;
using VideoOS.Mobile.SDK.Portable.Server.Base.Connection;
using VideoOS.Mobile.SDK.Portable.Server.Base.Video;
using VideoOS.Mobile.SDK.Portable.Server.ViewGroups;

namespace AICtrl
{
    [
   Guid("60074E37-9AD5-4E70-9AF0-69C19A496D58"),
   InterfaceType(ComInterfaceType.InterfaceIsIDispatch),
   ComVisible(true)
]
    public interface EventTest
    {
    };

    [
           Guid("92C06E45-3BAA-45D9-8AD1-94DFEE5DCCE7"),
           InterfaceType(ComInterfaceType.InterfaceIsDual),
           ComVisible(true)
    ]
    public interface MobileTest
    {
        [DispId(1)]
        int Test();
    };

    [
    Guid("9C5A4C2A-7DC8-440A-92CD-FF96A4E2454E"),
    ClassInterface(ClassInterfaceType.None),
    ComSourceInterfaces(typeof(EventTest)),
    ComDefaultInterface(typeof(MobileTest)),
    ComVisible(true)
    ]

    public partial class MobileControl : UserControl, MobileTest
    {
        public string uri = "";
        public string username = "";
        public string password = "";
        public UserType userType;
        private readonly List<ViewGroupTree> _listViewItems = new List<ViewGroupTree>();
        private LiveVideo _liveVideo = null;
        private PlaybackVideo _playbackVideo;
        private VideoPullProxy _videoPullProxy;
        private static Connection Connection { get; set; }
        private Guid _activeCameraId = Guid.Empty;

        public MobileControl()
        {
            InitializeComponent();
            VideoOS.Mobile.SDK.Portable.Environment.Instance.Initialize();

        }
        #region collecting memory - freeing resources
        // The Garbage Collector does not release a COM object that handles events.
        // To work around this problem, we run the garbage collector when the control
        // receives a WM_DESTROY message through its WindowProc function. 
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == 2) // WM_DESTROY
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        #endregion

        #region registering COM component
        [ComRegisterFunctionAttribute]
        public static void ComRegisterFunction(Type t)
        {
            InteropUtils.ComRegister(t);
        }

        [ComUnregisterFunctionAttribute]
        public static void ComUnregisterFunction(Type t)
        {
            InteropUtils.ComUnregister(t);
        }
        #endregion

        int MobileTest.Test()
        {
            return 0 ;
        }
        private UserType GetSelecetedAuthentication()
        {
            if (comboBoxAuthentication.InvokeRequired)
            {
                return (UserType)comboBoxAuthentication.Invoke(new Func<UserType>(GetSelecetedAuthentication));
            }
            else
            {
                return comboBoxAuthentication.SelectedItem.ToString().Equals("Windows authentication", StringComparison.InvariantCultureIgnoreCase) ?
                    UserType.ActiveDirectory : UserType.Basic;
            }
        }
        private void ConnectServer()
        {
            var channelType = ChannelTypes.HTTP;
            uri = uriText.Text;
            username = userText.Text;
            password = passwordText.Text;
            try
            {
                Connection = new Connection(channelType, uri, 8081);
                Connection.CommandsQueueing = CommandsQueueing.SingleThread;

                var connectResponse = Connection.Connect(null, TimeSpan.FromSeconds(15));
                if (connectResponse.ErrorCode != ErrorCodes.Ok)
                    throw new Exception("Not connected to surveillance server");        
                var loginResponse = Connection.LogIn(username, password, ClientTypes.MobileClient, TimeSpan.FromMinutes(2), GetSelecetedAuthentication());
                if (loginResponse.ErrorCode != ErrorCodes.Ok)
                    throw new Exception("Not loged in to the surveillance server");
            }
            catch
            {

            }
            Connection.RunHeartBeat = true;
   
        }

        #region View items initialization

        private void InitializeViews()
        {
            try
            {
                var allCamerasViews = ViewGroupsHelper.GetAllCamerasView(Connection.Views, new ViewParams(), TimeSpan.FromSeconds(15));

                ProcessViewItem(allCamerasViews);
                foreach (var name in _listViewItems)
                {
                    listBox1.Items.Add(name);
                }
               
            }
            catch (Exception)
            {
            }
        }

        private void ProcessViewItem(ViewGroupTree item)
        {
            if (item.ItemType == ViewItemType.Camera)
                _listViewItems.Add(item);

            foreach (var subItem in item.GetMembersList())
            {
                ProcessViewItem((ViewGroupTree)subItem);
            }
        }

        #endregion

        #region camera selection from the list

        private void CloseVideo()
        {
            _videoPullProxy?.Stop();
            _videoPullProxy = null;
            //_liveVideo?.Stop();
            //_liveVideo?.Dispose();
            //_liveVideo = null;
            _playbackVideo?.Stop();
            _playbackVideo?.Dispose();
            _playbackVideo = null;
            pictureBoxVideo.Image = null;
            UpdatePlaybackControls(0);
        }

        private void StartVideo(Guid cameraId)
        {
            var videoParams = new VideoParams()
            {
                CameraId = cameraId,
                DestWidth = pictureBoxVideo.Width,
                DestHeight = pictureBoxVideo.Height,
                CompressionLvl = 83,
                FPS = 60,
                // MethodType = StreamParamsHelper.MethodType.Push,
                MethodType = StreamParamsHelper.MethodType.Pull,
                // SignalType = StreamParamsHelper.SignalType.Live,
                SignalType = StreamParamsHelper.SignalType.Playback,
                StreamType = StreamParamsHelper.StreamType.Transcoded,
            };
            var response = Connection.Video.RequestStream(videoParams, null, TimeSpan.FromSeconds(30));
           
            var playbackresponse = Connection.Video.RequestStream(videoParams, null, OnRequestStreamSuccess, OnFail);
            if (response.ErrorCode != ErrorCodes.Ok)
            {
                MessageBox.Show("Error requesting stream from camera", response.ErrorCode.ToString());
                return;
            }
            ProcessStreamResponse(response);

           // _liveVideo = Connection.VideoFactory.CreateLiveVideo(new RequestStreamResponseLive(playbackresponse));
           //_liveVideo.NewFrame = OnNewFrame;
           // _liveVideo.Start();
        }
        private void OnFail(BaseCommandResponse responseParams)
        {
            MessageBox.Show("Error in MoS communication");
        }

        private void OnRequestStreamSuccess(RequestStreamResponse responseParams)
        {
            _playbackVideo = Connection.VideoFactory.CreatePlaybackVideo(new RequestStreamResponsePlayback(responseParams));
            _videoPullProxy = new VideoPullProxy(_playbackVideo, _playbackVideo, 15);
            _videoPullProxy.NewFrame = OnNewFrame;
            _videoPullProxy.Start();
            _playbackVideo.Start();
        }
        private void OnNewFrame(VideoFrame frame)
        {
            if (frame != null)
            {
                if (this.InvokeRequired)
                {
                    // invoke in correct (UX) context
                    BeginInvoke(new Action<VideoFrame>(OnNewFrame), new object[] { frame });
                }
                else
                {
                    if ((frame.Data != null) &&
                        (frame.Data.Any()))
                    {
                        using (var ms = new MemoryStream(frame.Data))
                        {
                            pictureBoxVideo.Image = new Bitmap(ms);
                        }
                    }
                    labelCurrentTime.Text = "Current time: " + TimeConverter.FromLong((long)frame.MainHeader.TimeStampUtcMs).ToString();

                    if (frame.ExtensionPresence(BinaryFrameHeaderHelper.HeaderExtensionFlags.PlaybackEvents))
                    {
                        UpdatePlaybackControls(frame.HeaderExtensionPlaybackEvents.CurrentFlags);
                    }

                }
            }
        }
        private void UpdatePlaybackControls(uint currentFlags)
        {
            var playbackStatus = currentFlags & (uint)PlaybackFlags.PlayMask;
            switch (playbackStatus)
            {
                case (uint)PlaybackFlags.PlayStopped:
                default:
                    buttonBack.Text = PlayBack;
                    buttonForward.Text = PlayNext;
                    break;

                case (uint)PlaybackFlags.PlayBackward:
                    buttonBack.Text = Pause;
                    buttonForward.Text = PlayNext;
                    break;

                case (uint)PlaybackFlags.PlayForward:
                    buttonBack.Text = PlayBack;
                    buttonForward.Text = Pause;
                    break;
            }
        }

        #endregion

        #region Close and Resize events

        private void FormLiveFormClosing(object sender, FormClosingEventArgs e)
        {
            CloseVideo();

            Connection.Dispose();
        }

        private void FormLiveResize(object sender, EventArgs e)
        {
            //_liveVideo?.StreamControl.ChangeStream(
            //    new VideoParams()
            //    {
            //        DestWidth = pictureBoxVideo.Width,
            //        DestHeight = pictureBoxVideo.Height,
            //    });
            _playbackVideo?.StreamControl.Rescale(pictureBoxVideo.Width, pictureBoxVideo.Height);

        }

        #endregion

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.Items.Count > 0)
            {
                var selIndex = listBox1.SelectedIndex;
                CloseVideo();
                _activeCameraId = _listViewItems[selIndex].CameraId;
                StartVideo(_activeCameraId);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ConnectServer();
            InitializeViews();
            this.Text = Connection.ServerFeatures.ServerDescription + " " + (Connection.ServerCapabilities.ServerProductCode == "1" ? "Main" : "LTSB");
            bindingSourceViewGroupTree.DataSource = _listViewItems;
            bindingSourceViewGroupTree.ResetBindings(false);

            EnablePtzMoveButtons(false);
            EnablePtzPresets(false);

        }
        private void EnablePtzMoveButtons(bool enabled)
        {
            buttonLeft.Enabled = buttonUp.Enabled = buttonRight.Enabled =
                buttonDown.Enabled = buttonHome.Enabled = buttonIn.Enabled = buttonOut.Enabled = enabled;
        }

        private void EnablePtzPresets(bool enabled)
        {
            comboBoxPreset.Items.Clear();
            comboBoxPreset.Enabled = enabled;

            if (enabled)
                UpdatePtzPresets();
        }
        private async void UpdatePtzPresets()
        {
            var response = await Connection.Ptz.GetPtzPresetsAsync(_activeCameraId, _maxTimeout);

            if (response.ErrorCode == ErrorCodes.NoPresetsAvailable)
                MessageBox.Show("No presets available");
            else
                ProcessMoveResponse(response);

            comboBoxPreset.Items.AddRange(response.Presets.ToArray());
        }
        private readonly TimeSpan _maxTimeout = TimeSpan.FromSeconds(30);

        private void buttonUp_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnCamera(PtzParamsHelper.PtzMoves.Up);
        }

        private void buttonHome_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnCamera(PtzParamsHelper.PtzMoves.Home);
        }

        private void buttonLeft_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnStream(PtzParamsHelper.PtzMoves.Left);
        }

        private void buttonDown_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnStream(PtzParamsHelper.PtzMoves.Down);
        }

        private void buttonRight_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnCamera(PtzParamsHelper.PtzMoves.Right);
        }

        private void buttonIn_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnCamera(PtzParamsHelper.PtzMoves.ZoomIn);
        }

        private void buttonOut_Click(object sender, EventArgs e)
        {
            ProcessPtzMoveOnStream(PtzParamsHelper.PtzMoves.ZoomOut);
        }
        private async void ProcessPtzMoveOnCamera(PtzParamsHelper.PtzMoves move)
        {
            var response = await Connection.Ptz.ControlPtzAsync(
                new PtzParams()
                {
                    CameraId = _activeCameraId,
                    PtzMoveType = PtzParamsHelper.PtzMoveTypes.Step,
                    PtzMove = move,
                },
                _maxTimeout);

            ProcessMoveResponse(response);
        }
        private async void ProcessPtzMoveOnStream(PtzParamsHelper.PtzMoves move)
        {
            var response = await _liveVideo.Ptz.ControlPtzAsync(
                new PtzParams()
                {
                    CameraId = _activeCameraId,
                    PtzMoveType = PtzParamsHelper.PtzMoveTypes.Step,
                    PtzMove = move,
                },
                _maxTimeout);

            ProcessMoveResponse(response);
        }
        private void ProcessMoveResponse(BaseCommandResponse response)
        {
            if (response.ErrorCode != ErrorCodes.Ok)
                MessageBox.Show("Error executing ptz command");
        }
        private void ProcessStreamResponse(BaseCommandResponse response)
        {
            var ptzMoveEnabled =
                response.OutputParams.ContainsKey(CommunicationCommands.AuthorizationPtz) &&
                response.OutputParams[CommunicationCommands.AuthorizationPtz] == CommunicationCommands.AuthorizationYes;
            EnablePtzMoveButtons(ptzMoveEnabled);

            var ptzPresetEnabled =
                response.OutputParams.ContainsKey(CommunicationCommands.AuthorizationPreset) &&
                response.OutputParams[CommunicationCommands.AuthorizationPreset] == CommunicationCommands.AuthorizationYes;
            EnablePtzPresets(ptzPresetEnabled);
        }
        private async void OnComboBoxPresetsSelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedPreset = comboBoxPreset.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedPreset))
                return;

            var response = await Connection.Ptz.ControlPtzAsync(
                new PtzParams() { CameraId = _activeCameraId, PtzPreset = selectedPreset }, _maxTimeout);

            ProcessMoveResponse(response);
        }

        private const string PlayBack = "<";
        private const string PlayNext = ">";
        private const string Pause = "||";

        private void OnButtonBackClick(object sender, EventArgs e)
        {
            OnButtonChangeSpeed(buttonBack, -1);
        }
        private void OnButtonChangeSpeed(Button button, int speed)
        {
            speed = (button.Text == Pause) ? 0 : speed;
            _playbackVideo?.PlaybackControl.ChangeSpeed(speed);
        }

        private void OnButtonForwardClick(object sender, EventArgs e)
        {
            OnButtonChangeSpeed(buttonForward, 1);
        }

        private void GoToTime(object sender, EventArgs e)
        {
            _playbackVideo?.PlaybackControl.GoToTime(dateTimePickerGoTo.Value);
        }
    }
}
