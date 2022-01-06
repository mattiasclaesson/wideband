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
using System.Threading;
using System.IO.Ports;
using System.IO;

namespace WidebandSupport
{

    public class LC1WidebandReader : IWidebandReader
    {

        private static BitPosition[] lambdaBitPositions = { 
                                                      new BitPosition(1, 1),
                                                      new BitPosition(2, 2),
                                                      new BitPosition(4, 4),
                                                      new BitPosition(8, 8),
                                                      new BitPosition(16, 16),
                                                      new BitPosition(32, 32),
                                                      new BitPosition(64, 64),
                                                      new BitPosition(256, 128),
                                                      new BitPosition(512, 256),
                                                      new BitPosition(1024, 512),
                                                      new BitPosition(2048, 1024),
                                                      new BitPosition(4096, 2048),
                                                      new BitPosition(8192, 4096) };

        private static BitPosition[] startStatusBitPositions = {     
                                                                new BitPosition(1024, 1),
                                                                new BitPosition(2048, 2),
                                                                new BitPosition(4096, 4)
                                                            };

        private static BitPosition[] startMultiplierBitPositions = {
                                                                new BitPosition(1, 1), 
                                                                new BitPosition(2, 2),
                                                                new BitPosition(4, 4),
                                                                new BitPosition(8, 8),
                                                                new BitPosition(16, 16),
                                                                new BitPosition(32, 32),
                                                                new BitPosition(64, 64),
                                                                new BitPosition(256, 128)
                                                                   };
        private static BitPosition[] headerLengthBitPositions = {
                                                                    new BitPosition(1, 1),
                                                                    new BitPosition(2, 2),
                                                                    new BitPosition(4, 4),
                                                                    new BitPosition(8, 8),
                                                                    new BitPosition(16, 16),
                                                                    new BitPosition(32, 32),
                                                                    new BitPosition(64, 64),
                                                                    new BitPosition(256, 128)
                                                                };


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

        int sampleBytePacketIndex = 0; // this is only used for testing.
        private bool testMode = false; // if true, test mode
        public bool TestMode
        {
            get { return testMode; }
            set { testMode = value; }
        }

        public LC1WidebandReader(String comPortName)
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
            comPort.BaudRate = 19200;
            comPort.DataBits = 8;
            comPort.Parity = Parity.None;
            comPort.StopBits = StopBits.One;
            comPort.Handshake = Handshake.None;

        }


        /*
         * This method is only used for testing
         */
        private int GetByteFromSamplePacket()
        {

            byte[] packet = new byte[] {     
                ( 128 | 32 | 16 | 2 ), // startHeader HiByte
                ( 128 | 2 ), // startHeader LoByte
                (64 | 2 | 1),  // LC1 startWord HiByte
                (16 | 2 | 1),  // LC1 startWord LoByte
                ( 2 | 1 ),  // LC1 lambda HiByte
                ( 64 | 32 | 16 | 4 ), // LC1 lambda LoByte
            };


            // HEADER START
            // byte[0] = "10011010"
            // byte[1] = "10000010"
            // length of subsequent words should be 2

            // FIRST WORD
            // byte[2] == "01000011"
            // byte[3] == "00010011"
            // start bit is set, multiplier is supposed to be 147

            // SECOND WORD (lambda Value is supposed to be 500)
            // byte[4] == "00000011"
            // byte[5] == "01110100"

            if (sampleBytePacketIndex >= packet.Length)
            {
                sampleBytePacketIndex = 0;
                Thread.Sleep(TimeSpan.FromMilliseconds(83));
            }

            return packet[sampleBytePacketIndex++];

        }

        private int GetLambda(int lambdaWord)
        {
            int lambdaValue = 0;

            foreach (BitPosition bp in lambdaBitPositions)
            {
                if ((lambdaWord & bp.Position) == bp.Position)
                {
                    lambdaValue |= bp.Value;
                }
            }

            return lambdaValue;
        }

        private int GetStatus(int startWord)
        {
            int status = 0;

            foreach (BitPosition bp in startStatusBitPositions)
            {
                if ((startWord & bp.Position) == bp.Position)
                {
                    status |= bp.Value;
                }
            }

            return status;
        }

        private int GetPacketLength(int headerWord)
        {
            int status = 0;

            foreach (BitPosition bp in headerLengthBitPositions)
            {
                if ((headerWord & bp.Position) == bp.Position)
                {
                    status |= bp.Value;
                }
            }

            return status;
        }

        private int GetWord(byte[] wordBytes)
        {

            int wordValue;

            // swap bytes to little endian

            Array.Reverse(wordBytes);

            wordValue = BitConverter.ToUInt16(wordBytes, 0);

            return wordValue;

        }

        private int GetMultiplier(int startWord)
        {
            int multiplier = 0;

            foreach (BitPosition bp in startMultiplierBitPositions)
            {
                if ((startWord & bp.Position) == bp.Position)
                {
                    multiplier |= bp.Value;
                }
            }

            return multiplier;


        }

        private bool IsHeaderHiByte(byte aByte)
        {
            bool isHigh = false;

            if ((aByte & 128) == 128 && (aByte & 32) == 32 && (aByte & 2) == 2)
            {
                isHigh = true;
            }

            return isHigh;

        }

        private void InitiateReading()
        {

            List<byte> buffer = new List<byte>();

            bool packetStarted = false, normalOperation = false;
            int multiplier = 0, packetLength = 0;

            try
            {
                comPort.Open();

                while (continueRunning)
                {
                    try
                    {

                        byte aByte = 0;

                        if (testMode)
                        {
                            aByte = (byte)GetByteFromSamplePacket(); // test packet
                        }
                        else
                        {
                            aByte = (byte)comPort.ReadByte(); // to read from the serial port
                        }

                        if ((aByte & 128) == 128 && buffer.Count > 0 && true == IsHeaderHiByte(buffer[buffer.Count - 1]))
                        {

                            // must be the Lo of the header word because bit 7 is 1 and previous byte 
                            // was header hi byte

                            // store before we clear buffer
                            byte hiByte = buffer[buffer.Count - 1];

                            buffer.Clear();

                            // add previous Hi Byte
                            buffer.Add(hiByte);

                            // add Lo Byte
                            buffer.Add(aByte);

                            // ISP2 has a packet length, retrieve it
                            byte[] headerWordBytes = new byte[2] { buffer[0], buffer[1] };

                            packetLength = GetPacketLength(GetWord(headerWordBytes));

                            packetStarted = true;
                            normalOperation = false;

                        }
                        else if (true == IsHeaderHiByte(aByte))
                        {
                            // must be the Hi of the header word because bit 7 is 1, bit 5 is 1, and bit 1 is 1
                            // eq.  10100010

                            buffer.Add(aByte);
                        }
                        else
                        {

                            if (true == packetStarted && buffer.Count <= ((packetLength * 2) + 2))
                            {

                                buffer.Add(aByte);

                                switch (buffer.Count)
                                {

                                    case 4:
                                        {
                                            byte[] startWordBytes = new byte[2] { buffer[2], buffer[3] };
                                            int startWord = GetWord(startWordBytes);
                                            int status = GetStatus(startWord);
                                            if (0 == status)
                                            {
                                                // 000 is normal operation
                                                multiplier = GetMultiplier(startWord);
                                                normalOperation = true;
                                            }
                                            else
                                            {
                                                // 001 Lambda value contains O2 level in 1/10%
                                                // 010 Free air Calib in progress, Lambda data not valid
                                                // 011 Need Free Air Calibration Request, Lambda Data not valid
                                                // 100 Warming up, Lambda value is temp in 1/10% of operating temp
                                                // 101 Heater Calibration, Lambda value contains calibration countdown
                                                // 111 Reserved

                                                if (normalOperation == true)
                                                {
                                                    normalOperation = false;
                                                    latestReading = 0;
                                                }
                                            }

                                        }

                                        break;

                                    case 6:
                                        {
                                            byte[] lambdaWordBytes = new byte[2] { buffer[4], buffer[5] };
                                            int lambdaWord = GetWord(lambdaWordBytes);
                                            latestReading = (GetLambda(lambdaWord) + 500) * multiplier / 10000d;
                                        }

                                        break;

                                }

                            }
                            else
                            {
                                // ignore until next header word is encountered, reset the flow control variables to sane values.
                                packetLength = 0;
                                normalOperation = false;
                                packetStarted = false;
                            }

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

        class BitPosition
        {
            private int position;

            public int Position
            {
                get { return position; }
            }

            private int value;

            public int Value
            {
                get { return value; }
            }

            public BitPosition(int bitPosition, int bitValue)
            {
                this.position = bitPosition;
                this.value = bitValue;
            }
        }
    }
}
