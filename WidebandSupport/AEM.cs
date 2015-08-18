/*
 * Copyright 2009 George Daswani
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0 
 *     
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License. 
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO.Ports;
using System.IO;

namespace WidebandSupport
{

    public class AEMWidebandReader : IWidebandReader
    {
        private Object locker = new Object();

        private double latestReading;
        public double LatestReading
        {
            get { return latestReading; }
        }

        private SerialPort comPort;
        private Thread worker;
        bool continueRunning = false;

        // test related bits

        int sampleLinePacketIndex = 0; // this is only used for testing.
        private bool testMode = false; // if true, test mode
        public bool TestMode
        {
            get { return testMode; }
            set { testMode = value; }
        }

        public AEMWidebandReader(String comPortName)
        {

            if (false == IsSerialPortNameValid(comPortName))
            {
                throw new ArgumentException(comPortName + ", is invalid.");
            }

            init(comPortName);

        }

        public void Dispose()
        {
            if (worker != null && worker.IsAlive && true == continueRunning)
            {
                Stop();
            }

        }

        private bool IsSerialPortNameValid(String comPortName)
        {

            bool serialPortNameValid = false;

            foreach (String serialPortName in SerialPort.GetPortNames())
            {
                if (serialPortName.Equals(comPortName))
                {
                    serialPortNameValid = true;
                }
            }

            return serialPortNameValid;
        }

        private void init(String comPortName)
        {

            comPort = new SerialPort();
            comPort.PortName = comPortName;
            comPort.BaudRate = 9600;
            comPort.DataBits = 8;
            comPort.Parity = Parity.None;
            comPort.StopBits = StopBits.One;
            comPort.Handshake = Handshake.RequestToSend;

        }


        /*
         * This method is only used for testing
         */
        private String GetLineFromSamplePacket()
        {
            String[] lines = { 
                "AEM Inc. 2003",
                "AFR Gauge",
                "Version 3",
                "",
                "00.0",
                "10.0",
                "11.0",
                "12.0",
                "13.0",
                "14.0",
                "15.0"
                 };

            if (sampleLinePacketIndex >= lines.Length)
            {
                sampleLinePacketIndex = 0;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));

            return lines[sampleLinePacketIndex++];

        }

        private void InitiateReading()
        {

            try
            {
                comPort.Open();

                while (continueRunning)
                {

                    try
                    {
                        String line = null;
                        double afrValue;

                        if (testMode)
                        {
                            line = GetLineFromSamplePacket();
                        }
                        else
                        {
                            line = comPort.ReadLine();
                        }

                        if (line != null && 0 != line.Trim().Length && true == Double.TryParse(line, out afrValue))
                        {
                            latestReading = afrValue;
                        }
                    }
                    catch (ThreadInterruptedException)
                    {
                        // nothing
                    }

                }

            }
            catch (IOException)
            {
                // ignore
            }
            finally
            {
                comPort.Close();
            }
        }

        public void Start()
        {
            lock (locker)
            {
                if (worker == null || worker.IsAlive == false)
                {
                    continueRunning = true;
                    worker = new Thread(new ThreadStart(InitiateReading));
                    worker.Start();
                }
                else
                {
                    throw new InvalidOperationException("Already started.");
                }
            }

        }

        public void Stop()
        {
            lock (locker)
            {

                if (continueRunning == true)
                {
                    continueRunning = false;
                    worker.Join(TimeSpan.FromSeconds(5));

                    if (worker.IsAlive)
                    {
                        // if worker is still alive, most likely still blocked on readByte, interrupt
                        worker.Interrupt();
                    }

                }
                else
                {
                    throw new InvalidOperationException("Not started.");
                }
            }
        }

    }
}
