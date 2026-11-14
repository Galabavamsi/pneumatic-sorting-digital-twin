<#
.SYNOPSIS
  Reads Kit 1 variables straight from the Micro850 over EtherNet/IP. Read-only.

.DESCRIPTION
  Finds the controller with the standard EtherNet/IP "List Identity" broadcast (its
  169.254.x.x address changes on every power-up), opens a CIP connection and reads
  global variables by name. It never writes to the controller.

  No CCW setup is needed: Micro800 global variables are readable by name.

.EXAMPLE
  .\Read-PlcVariables.ps1                 # status summary
.EXAMPLE
  .\Read-PlcVariables.ps1 -All            # every Kit 1 global plus the raw I/O points
.EXAMPLE
  .\Read-PlcVariables.ps1 -Watch 20       # print changes for 20 seconds
#>
param(
    [string]$PlcHost,
    [switch]$All,
    [int]$Watch = 0,
    [string]$CsvPath = (Join-Path $PSScriptRoot 'Kit1_Variables.csv')
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

public class Micro800Reader : IDisposable
{
    private TcpClient tcp;
    private NetworkStream s;
    private uint session;
    private uint connId;
    private ushort seq;

    public void Open(string host, string localIp, int timeoutMs)
    {
        tcp = string.IsNullOrEmpty(localIp)
            ? new TcpClient()
            : new TcpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(localIp), 0));
        IAsyncResult ar = tcp.BeginConnect(host, 44818, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) throw new Exception("TCP connect to " + host + " timed out");
        tcp.EndConnect(ar);
        tcp.NoDelay = true;
        s = tcp.GetStream();
        s.ReadTimeout = timeoutMs;
        s.WriteTimeout = timeoutMs;

        byte[] reply = Encap(0x65, 0, new byte[] { 1, 0, 0, 0 });   // RegisterSession
        session = BitConverter.ToUInt32(reply, 4);
        if (session == 0) throw new Exception("RegisterSession was refused");
        ForwardOpen();
    }

    private byte[] ReadExact(int n)
    {
        byte[] b = new byte[n];
        int got = 0;
        while (got < n)
        {
            int r = s.Read(b, got, n - got);
            if (r <= 0) throw new Exception("connection closed by the controller");
            got += r;
        }
        return b;
    }

    private byte[] Encap(ushort command, uint sessionHandle, byte[] data)
    {
        byte[] pkt = new byte[24 + data.Length];
        BitConverter.GetBytes(command).CopyTo(pkt, 0);
        BitConverter.GetBytes((ushort)data.Length).CopyTo(pkt, 2);
        BitConverter.GetBytes(sessionHandle).CopyTo(pkt, 4);
        data.CopyTo(pkt, 24);
        s.Write(pkt, 0, pkt.Length);

        byte[] hdr = ReadExact(24);
        int len = BitConverter.ToUInt16(hdr, 2);
        uint status = BitConverter.ToUInt32(hdr, 8);
        byte[] body = ReadExact(len);
        if (status != 0) throw new Exception("EtherNet/IP status 0x" + status.ToString("X"));
        byte[] all = new byte[24 + len];
        hdr.CopyTo(all, 0);
        body.CopyTo(all, 24);
        return all;
    }

    private static byte[] ExtractItem(byte[] reply, ushort wantedType, int skip)
    {
        int p = 24 + 6;
        int count = BitConverter.ToUInt16(reply, p);
        p += 2;
        for (int i = 0; i < count; i++)
        {
            ushort type = BitConverter.ToUInt16(reply, p);
            int len = BitConverter.ToUInt16(reply, p + 2);
            p += 4;
            if (type == wantedType)
            {
                byte[] d = new byte[len - skip];
                Array.Copy(reply, p + skip, d, 0, len - skip);
                return d;
            }
            p += len;
        }
        throw new Exception("reply item 0x" + wantedType.ToString("X") + " not found");
    }

    private byte[] Unconnected(byte[] cip)
    {
        MemoryStream m = new MemoryStream();
        BinaryWriter w = new BinaryWriter(m);
        w.Write((uint)0);
        w.Write((ushort)10);
        w.Write((ushort)2);
        w.Write((ushort)0x0000); w.Write((ushort)0);
        w.Write((ushort)0x00B2); w.Write((ushort)cip.Length);
        w.Write(cip);
        return ExtractItem(Encap(0x6F, session, m.ToArray()), 0x00B2, 0);
    }

    private byte[] Connected(byte[] cip)
    {
        seq++;
        MemoryStream m = new MemoryStream();
        BinaryWriter w = new BinaryWriter(m);
        w.Write((uint)0);
        w.Write((ushort)0);
        w.Write((ushort)2);
        w.Write((ushort)0x00A1); w.Write((ushort)4); w.Write(connId);
        w.Write((ushort)0x00B1); w.Write((ushort)(cip.Length + 2)); w.Write(seq);
        w.Write(cip);
        return ExtractItem(Encap(0x70, session, m.ToArray()), 0x00B1, 2);
    }

    private void ForwardOpen()
    {
        Random rnd = new Random();
        MemoryStream m = new MemoryStream();
        BinaryWriter w = new BinaryWriter(m);
        w.Write((byte)0x54); w.Write((byte)2); w.Write(new byte[] { 0x20, 0x06, 0x24, 0x01 });
        w.Write((byte)0x0A); w.Write((byte)0x0E);
        w.Write((uint)0);
        w.Write((uint)rnd.Next(1, int.MaxValue));
        w.Write((ushort)rnd.Next(1, 0xFFFF));
        w.Write((ushort)0x1337);
        w.Write((uint)0x4B495431);
        w.Write((byte)1); w.Write(new byte[3]);
        w.Write((uint)2000000); w.Write((ushort)0x43F4);
        w.Write((uint)2000000); w.Write((ushort)0x43F4);
        w.Write((byte)0xA3);
        w.Write((byte)2); w.Write(new byte[] { 0x20, 0x02, 0x24, 0x01 });
        byte[] r = Unconnected(m.ToArray());
        if (r[2] != 0)
        {
            string ext = r[3] > 0 ? " ext 0x" + BitConverter.ToUInt16(r, 4).ToString("X4") : "";
            throw new Exception("Forward Open refused: status 0x" + r[2].ToString("X2") + ext);
        }
        connId = BitConverter.ToUInt32(r, 4 + 2 * r[3]);
    }

    // Returns bool, a number, or a string starting with "ERR" / "type".
    public object Read(string name)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        int pad = nb.Length % 2;
        MemoryStream m = new MemoryStream();
        BinaryWriter w = new BinaryWriter(m);
        w.Write((byte)0x4C);
        w.Write((byte)((2 + nb.Length + pad) / 2));
        w.Write((byte)0x91); w.Write((byte)nb.Length); w.Write(nb);
        if (pad == 1) w.Write((byte)0);
        w.Write((ushort)1);
        byte[] r = Connected(m.ToArray());
        if (r[2] != 0)
        {
            string ext = r[3] > 0 ? " ext 0x" + BitConverter.ToUInt16(r, 4).ToString("X4") : "";
            return "ERR 0x" + r[2].ToString("X2") + ext;
        }
        int p = 4 + 2 * r[3];
        ushort type = BitConverter.ToUInt16(r, p);
        p += 2;
        switch (type)
        {
            case 0x00C1: return r[p] != 0;
            case 0x00C2: return (int)(sbyte)r[p];
            case 0x00C3: return (int)BitConverter.ToInt16(r, p);
            case 0x00C4: return BitConverter.ToInt32(r, p);
            case 0x00C6: return (int)r[p];
            case 0x00C7: return (int)BitConverter.ToUInt16(r, p);
            case 0x00C8: return (long)BitConverter.ToUInt32(r, p);
            case 0x00CA: return BitConverter.ToSingle(r, p);
            default: return "type 0x" + type.ToString("X4") + " " + BitConverter.ToString(r, p, r.Length - p);
        }
    }

    public void Dispose()
    {
        try { if (s != null && session != 0) { byte[] pkt = new byte[24]; pkt[0] = 0x66; BitConverter.GetBytes(session).CopyTo(pkt, 4); s.Write(pkt, 0, 24); } } catch { }
        try { if (tcp != null) tcp.Close(); } catch { }
    }
}
'@

function Find-Plc {
    $pkt = New-Object byte[] 24
    $pkt[0] = 0x63
    $found = @{}
    $locals = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.AddressState -eq 'Preferred' }
    foreach ($l in $locals) {
        $udp = New-Object System.Net.Sockets.UdpClient
        try {
            $udp.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::Broadcast, $true)
            $udp.Client.Bind((New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Parse($l.IPAddress), 0)))
            $udp.Client.ReceiveTimeout = 500
            # Limited broadcast and the subnet's own broadcast address: some stacks answer only one.
            $ipBytes = [System.Net.IPAddress]::Parse($l.IPAddress).GetAddressBytes()
            $mask = [uint32]0
            if ($l.PrefixLength -gt 0) { $mask = [uint32]::MaxValue -shl (32 - $l.PrefixLength) }
            $ipNum = ([uint32]$ipBytes[0] -shl 24) -bor ([uint32]$ipBytes[1] -shl 16) -bor ([uint32]$ipBytes[2] -shl 8) -bor [uint32]$ipBytes[3]
            $bc = $ipNum -bor (-bnot $mask -band [uint32]::MaxValue)
            $directed = '{0}.{1}.{2}.{3}' -f (($bc -shr 24) -band 255), (($bc -shr 16) -band 255), (($bc -shr 8) -band 255), ($bc -band 255)
            foreach ($dest in @('255.255.255.255', $directed)) {
                [void]$udp.Send($pkt, 24, (New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Parse($dest), 44818)))
            }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            while ($sw.ElapsedMilliseconds -lt 2500) {
                try {
                    $ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
                    $r = $udp.Receive([ref]$ep)
                    if ($r.Length -gt 63) {
                        $found[$ep.Address.ToString()] = @{ Name = [System.Text.Encoding]::ASCII.GetString($r, 63, [Math]::Min([int]$r[62], $r.Length - 63)); Local = $l.IPAddress }
                    }
                } catch [System.Net.Sockets.SocketException] { }
            }
        } catch { } finally { $udp.Close() }
    }
    $found
}

if (-not $PlcHost) {
    $devices = Find-Plc
    $plc = $devices.GetEnumerator() | Where-Object { $_.Value.Name -like '2080-*' } | Select-Object -First 1
    if (-not $plc) { throw 'No Micro800 answered the EtherNet/IP broadcast. Is the panel on and the cable plugged in?' }
    $PlcHost = $plc.Key
    $LocalIp = $plc.Value.Local
    "PLC found: $PlcHost ($($plc.Value.Name)) via $LocalIp"
}

$status = @(
    'HR_HEARTBEAT', 'HR_STEP', 'HR_FAULTCODE', 'HR_MATERIAL', 'HR_PARTSDONE', 'HR_METALCOUNT', 'HR_PLASTICCOUNT',
    'ST_AUTORUNNING', 'ST_FAULTED', 'ST_HOMED',
    'IN_ESTOPOK', 'IN_STARTPB', 'IN_STOPPB', 'IN_RESETPB', 'IN_CONTINUOUSMODE',
    'IN_FEEDRETRACTED', 'IN_FEEDEXTENDED', 'IN_UPPEREJRETRACTED', 'IN_UPPEREJEXTENDED',
    'IN_LOWEREJRETRACTED', 'IN_LOWEREJEXTENDED', 'IN_LIFTUP', 'IN_LIFTDOWN',
    'IN_INDUCTIVE', 'IN_CAPACITIVE', 'IN_PARTONLIFT', 'IN_MAGAZINEPARTPRESENT', 'IN_MAGAZINEEMPTY',
    'OUT_FEEDEXTEND', 'OUT_FEEDRETRACT', 'OUT_UPPEREJEXTEND', 'OUT_UPPEREJRETRACT',
    'OUT_LOWEREJEXTEND', 'OUT_LOWEREJRETRACT', 'OUT_LIFTUP', 'OUT_LIFTDOWN'
)
$names = $status
if ($All) {
    $csv = Import-Csv $CsvPath | Where-Object { $_.scope -eq 'Global' } | ForEach-Object { $_.name.ToUpperInvariant() }
    $io = (0..13 | ForEach-Object { '_IO_EM_DI_{0:D2}' -f $_ }) + (0..7 | ForEach-Object { '_IO_EM_DO_{0:D2}' -f $_ })
    $names = @($status + $csv + $io | Select-Object -Unique)
}

$reader = New-Object Micro800Reader
try {
    $reader.Open($PlcHost, $LocalIp, 3000)
    if ($Watch -gt 0) {
        $last = @{}
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        "Watching for $Watch s. Only changes are printed (heartbeat left out)."
        while ($sw.Elapsed.TotalSeconds -lt $Watch) {
            foreach ($n in $names) {
                if ($n -eq 'HR_HEARTBEAT') { continue }
                $v = $reader.Read($n)
                if (-not $last.ContainsKey($n) -or "$($last[$n])" -ne "$v") {
                    '{0,7:N2} s  {1,-24} {2}' -f $sw.Elapsed.TotalSeconds, $n, $v
                    $last[$n] = $v
                }
            }
        }
    } else {
        foreach ($n in $names) { '{0,-24} {1}' -f $n, $reader.Read($n) }
    }
} finally {
    $reader.Dispose()
}
