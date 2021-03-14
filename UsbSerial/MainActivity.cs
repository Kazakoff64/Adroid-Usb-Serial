using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Util;
using Hoho.Android.UsbSerial.Driver;
using Hoho.Android.UsbSerial.Extensions;
using Hoho.Android.UsbSerial.Util;

[assembly: UsesFeature("android.hardware.usb.host")]

namespace UsbSerial
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    [IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
    [MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
    public class MainActivity : Activity
    {

        static readonly string TAG = typeof(MainActivity).Name;
        const string ACTION_USB_PERMISSION = "com.hoho.android.usbserial.examples.USB_PERMISSION";

        UsbManager usbManager;

        public const string EXTRA_TAG = "PortInfo";

        const int READ_WAIT_MILLIS = 200;
        const int WRITE_WAIT_MILLIS = 200;

        int ItemPort = 0;

        SerialInputOutputManager serialIoManager;

        UsbSerialPortAdapter adapter;
        BroadcastReceiver detachedReceiver;
        UsbSerialPort selectedPort;

        UsbSerialPort selectedPortTest;

        UsbSerialPort port;

        Button button;

        TextView textView1;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            //Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
			

			usbManager = GetSystemService(Context.UsbService) as UsbManager;
            Button wakeButton = FindViewById<Button>(Resource.Id.button2);

            button = FindViewById<Button>(Resource.Id.button1);

            textView1 = FindViewById<TextView>(Resource.Id.textView1);



            byte[] sleepdata = new byte[] { 0xf0, 0x04, 0x10, 0xf1 };
            byte[] wakedata = new byte[] { 0xf0, 0x04, 0x11, 0xf1 };

            //sleepButton.Click += delegate
            //{
            //    WriteData(sleepdata);
            //};

            wakeButton.Click += delegate
            {
                WriteData(sleepdata);
            };

        }

        protected override async void OnResume()
        {
            base.OnResume();

            adapter = new UsbSerialPortAdapter(this);         

            button.Click += async (sender, e) => {
                await OnItemClick(sender);
            };

            await PopulateListAsync();

            //register the broadcast receivers
            detachedReceiver = new UsbDeviceDetachedReceiver(this);
            RegisterReceiver(detachedReceiver, new IntentFilter(UsbManager.ActionUsbDeviceDetached));
        }


        protected override void OnPause()
        {
            base.OnPause();

            // unregister the broadcast receivers
            var temp = detachedReceiver; // copy reference for thread safety
            if (temp != null)
                UnregisterReceiver(temp);
        }
        internal static Task<IList<IUsbSerialDriver>> FindAllDriversAsync(UsbManager usbManager)
        {
            // using the default probe table
            // return UsbSerialProber.DefaultProber.FindAllDriversAsync (usbManager);

            // adding a custom driver to the default probe table
            var table = UsbSerialProber.DefaultProbeTable;
            table.AddProduct(0x1b4f, 0x0008, typeof(CdcAcmSerialDriver)); // IOIO OTG

            table.AddProduct(0x09D8, 0x0420, typeof(CdcAcmSerialDriver)); // Elatec TWN4

            var prober = new UsbSerialProber(table);
            return prober.FindAllDriversAsync(usbManager);
        }

        public void MessageBox(string MyMessage)
        {

            Android.App.AlertDialog.Builder builder;
            builder = new Android.App.AlertDialog.Builder(this);
            builder.SetTitle("Сообщение");
            builder.SetMessage(MyMessage);
            builder.SetCancelable(false);
            builder.SetPositiveButton("OK", delegate { });
            Dialog dialog = builder.Create();
            dialog.Show();
            return;
        }


        async Task OnItemClick(object sender)
        {

            for (int i = 0; i < adapter.Count; i++)
            {
                selectedPortTest = adapter.GetItem(i);

                if (selectedPortTest.Driver.Device.VendorId == 6790 && selectedPortTest.Driver.Device.DeviceId == 1003) {

                    ItemPort = i;
                
                } 

            }


            selectedPort = adapter.GetItem(ItemPort);
            var permissionGranted = await usbManager.RequestPermissionAsync(selectedPort.Driver.Device, this);
            if (permissionGranted)
            {

                //MessageBox("Доступ к USB получен!");

                int vendorId = selectedPort.Driver.Device.VendorId;
                int deviceId = selectedPort.Driver.Device.DeviceId;
                int portNumber = 0;


                var drivers = await FindAllDriversAsync(usbManager);

                var driver = drivers.Where((d) => d.Device.VendorId == vendorId && d.Device.DeviceId == deviceId).FirstOrDefault();
                if (driver == null)
                    throw new Exception("Driver specified in extra tag not found.");

                port = driver.Ports[portNumber];
                if (port == null)
                {
                    MessageBox("No serial device.");
                    return;
                }

                serialIoManager = new SerialInputOutputManager(port)
                {
                    BaudRate = 115200,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                };
                serialIoManager.DataReceived += (sender, e) =>
                {
                    RunOnUiThread(() =>
                    {
                        UpdateReceivedData(e.Data);
                    });
                };
                serialIoManager.ErrorReceived += (sender, e) =>
                {
                    RunOnUiThread(() =>
                    {
                        var intent = new Intent(this, typeof(MainActivity));
                        StartActivity(intent);
                    });
                };

                try
                {
                    serialIoManager.Open(usbManager);
                }
                catch (Java.IO.IOException e)
                {
                    MessageBox("Error opening device: " + e.Message);
                    return;
                }

            }
        }


        void WriteData(byte[] data)
        {
            if (serialIoManager.IsOpen)
            {
                port.Write(data, WRITE_WAIT_MILLIS);
            }
        }

        void UpdateReceivedData(byte[] data)
        {
            string result = System.Text.Encoding.UTF8.GetString(data);
            textView1.Text = result;

        }

        async Task PopulateListAsync()
        {
            Log.Info(TAG, "Refreshing device list ...");

            var drivers = await FindAllDriversAsync(usbManager);

            adapter.Clear();
            foreach (var driver in drivers)
            {
                var ports = driver.Ports;
                Log.Info(TAG, string.Format("+ {0}: {1} port{2}", driver, ports.Count, ports.Count == 1 ? string.Empty : "s"));
                foreach (var port in ports)
                    adapter.Add(port);
            }

            adapter.NotifyDataSetChanged();
            Log.Info(TAG, "Done refreshing, " + adapter.Count + " entries found.");
        }

        #region UsbSerialPortAdapter implementation

        class UsbSerialPortAdapter : ArrayAdapter<UsbSerialPort>
        {
            public UsbSerialPortAdapter(Context context)
                : base(context, global::Android.Resource.Layout.SimpleExpandableListItem2)
            {
            }

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                var row = convertView;
                if (row == null)
                {
                    var inflater = Context.GetSystemService(Context.LayoutInflaterService) as LayoutInflater;
                    row = inflater.Inflate(global::Android.Resource.Layout.SimpleListItem2, null);
                }

                var port = this.GetItem(position);
                var driver = port.GetDriver();
                var device = driver.GetDevice();

                var title = string.Format("Vendor {0} Product {1}",
                    HexDump.ToHexString((short)device.VendorId),
                    HexDump.ToHexString((short)device.ProductId));
                row.FindViewById<TextView>(global::Android.Resource.Id.Text1).Text = title;

                var subtitle = device.Class.SimpleName;
                row.FindViewById<TextView>(global::Android.Resource.Id.Text2).Text = subtitle;

                return row;
            }
        }

        #endregion

        #region UsbDeviceDetachedReceiver implementation

        class UsbDeviceDetachedReceiver
            : BroadcastReceiver
        {
            readonly string TAG = typeof(UsbDeviceDetachedReceiver).Name;
            readonly MainActivity activity;

            public UsbDeviceDetachedReceiver(MainActivity activity)
            {
                this.activity = activity;
            }

            public async override void OnReceive(Context context, Intent intent)
            {
                var device = intent.GetParcelableExtra(UsbManager.ExtraDevice) as UsbDevice;
                await activity.PopulateListAsync();
            }
        }

        #endregion


        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}