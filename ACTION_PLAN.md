# Action Plan - Get Modbus Data Flowing

## TL;DR

**Your PC network is fine. The problem is the Modbus devices (GH-3, GH-4, GH-5) are not responding.**

- ✅ PC is on correct network (172.133.x.x)
- ✅ PC can reach the network
- ✅ Database is ready
- ❌ Modbus devices aren't answering

**ACTION:** Check if devices have power and are online.

---

## Quick Diagnosis Steps

### Step 1: Check Device Power (2 minutes)
Go to the furnace area where GH-3, GH-4, GH-5 controllers are located:

- [ ] **GH-3 at 172.133.230.7**
  - Does it have a power light on?
  - Does it show any error codes?
  - Is the ethernet cable connected?

- [ ] **GH-4 at 172.133.0.149**
  - Does it have a power light on?
  - Does it show any error codes?
  - Is the ethernet cable connected?

- [ ] **GH-5 at 172.133.0.149**
  - Does it have a power light on?
  - Does it show any error codes?
  - Is the ethernet cable connected?

### Step 2: Test Network Connectivity (from your PC)

Open PowerShell and run:

```powershell
# Test if devices respond
ping 172.133.230.7
ping 172.133.0.149

# Test Modbus port specifically
Test-NetConnection -ComputerName 172.133.230.7 -Port 502
Test-NetConnection -ComputerName 172.133.0.149 -Port 502
```

**Expected Results:**
- If devices are online: `Reply from 172.133.x.x` ✅
- If devices are offline: `Request timed out` ❌

### Step 3: Power On Devices (if needed)

If devices are off:
1. Locate the power switch on each device
2. Switch to ON position
3. Wait 30-60 seconds for boot-up
4. Check for status LEDs

---

## Likely Issues & Solutions

### Issue 1: Devices Are Powered OFF
**Symptom:** No LED lights, no response to ping  
**Solution:**  
1. Check power cable is connected
2. Check power supply outlet has power
3. Turn on the power switch
4. Wait for device to boot

**Expected:** Device comes online within 30-60 seconds

---

### Issue 2: Ethernet Cable Disconnected
**Symptom:** Device powers on but still no response  
**Solution:**  
1. Check ethernet cable physical connection
2. Verify cable goes to network switch
3. Look for cable damage
4. Try a different cable if available
5. Restart the device

**Expected:** Device connects to network

---

### Issue 3: Wrong IP Addresses
**Symptom:** Devices are online but app can't connect  
**Solution:**  
1. Check the device IP configuration (check device menu/display)
2. Verify IPs match appsettings.json:
   - GH-3 should be 172.133.230.7
   - GH-4 should be 172.133.0.149
   - GH-5 should be 172.133.0.149
3. If different, update appsettings.json with correct IPs

**File to edit:** `appsettings.json`
```json
"Controllers": [
  {
	"Id": "GH-3",
	"Host": "172.133.230.7",  // Change if needed
	"Port": 502
  }
]
```

---

### Issue 4: Port 502 Blocked
**Symptom:** Device responds to ping but not to TCP 502  
**Solution:**  
1. Check Windows Firewall settings
2. Allow port 502 through firewall
3. Check network firewall/ACL rules
4. Contact network administrator if corporate firewall

**To allow in Windows Firewall:**
```powershell
New-NetFirewallRule -DisplayName "Allow Modbus" -Direction Inbound `
  -LocalPort 502 -Protocol TCP -Action Allow
```

---

## Data Collection Flow (Once Devices Are Online)

```
Modbus Device (GH-3, GH-4, GH-5)
		↓ (Modbus registers)
Application (polls every 5 sec)
		↓ (reads all channels)
Creates Snapshot
  {
	"Timestamp": "2026-10-28 10:40:00",
	"FurnaceNo": "GH-3",
	"ChannelDataJson": {
	  "Channel1": 1200.5,
	  "Channel2": 1201.2,
	  "Channel3": 1199.8
	}
  }
		↓
Database (FurnaceTemperatureSnapshots table)
		↓
Query Results (by date & temperature)
```

---

## Testing After Devices Come Online

### Test 1: Check Application Logs
Look for messages like:
```
Successfully read from GH-3 at 172.133.230.7
Successfully read from GH-4 at 172.133.0.149
Successfully read from GH-5 at 172.133.0.149
Stored snapshot for GH-3 with 3 channels
```

### Test 2: Query Database
```powershell
# Check if real data is appearing
$connectionString = "Server=(localdb)\MSSQLLocalDB;Database=HALHeatTreatmentCard;Trusted_Connection=True;TrustServerCertificate=True;"

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.Open()

$query = "SELECT COUNT(*) FROM dbo.FurnaceTemperatureSnapshots WHERE FurnaceNo IN ('GH-3', 'GH-4', 'GH-5')"
$command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
$result = $command.ExecuteScalar()

Write-Host "Records from real devices: $result"
$connection.Close()
```

If count > 0, data is flowing! ✅

### Test 3: Check Latest Data
```sql
SELECT TOP 10 Timestamp, FurnaceNo, ChannelDataJson
FROM dbo.FurnaceTemperatureSnapshots
WHERE FurnaceNo IN ('GH-3', 'GH-4', 'GH-5')
ORDER BY Timestamp DESC
```

---

## Troubleshooting Checklist

**Priority 1: Power & Physical**
- [ ] All devices have power cable connected
- [ ] Power outlet has power (test with another device)
- [ ] Power switches are ON
- [ ] All ethernet cables are connected firmly

**Priority 2: Network**
- [ ] Ping 172.133.230.7 responds
- [ ] Ping 172.133.0.149 responds
- [ ] TCP port 502 is open (Test-NetConnection)
- [ ] No firewall blocking port 502

**Priority 3: Configuration**
- [ ] IP addresses in appsettings.json match devices
- [ ] Port 502 is correct (not changed)
- [ ] Controller IDs match: GH-3, GH-4, GH-5
- [ ] Polling is enabled in config

**Priority 4: Application**
- [ ] Application is running (check Process Explorer)
- [ ] Application has no errors (check logs)
- [ ] Database table exists (SELECT * FROM FurnaceTemperatureSnapshots)

---

## Getting Help

If devices still don't work after checking all above:

1. **Collect Info:**
   - Device IP addresses (from device display)
   - Device status lights (any error codes?)
   - Network switch status
   - Router status

2. **Check Logs:**
   - Application error logs
   - System event viewer
   - Network trace (optional)

3. **Contact:**
   - Network administrator (if corporate network)
   - Device manufacturer support (if device issue)
   - Your development team

---

## Summary

| Step | Status | Next Action |
|------|--------|------------|
| PC Network | ✅ Working | None needed |
| Database | ✅ Ready | None needed |
| Application | ✅ Running | None needed |
| **Modbus Devices** | ❌ Offline | **CHECK POWER & CONNECTION** |

**Once devices are online, data will flow automatically!** 🚀

---

## Expected Timeline

- **5 seconds after device comes online:** Application connects
- **10 seconds after:** First snapshot stored in database
- **15+ seconds:** Data visible when you query the database
- **Every 5 seconds:** New snapshots arrive automatically

Once this happens, you'll see:
- Database filling with FurnaceTemperatureSnapshots
- Queries by date and temperature returning real furnace data
- Complete channel consistency (all channels together)
- Accurate temperature readings from all devices

---

## Quick Reference: Network Details

**Your PC:**
- Ethernet: 172.133.63.255 / 169.254.78.247 ✅
- Wi-Fi: 10.58.203.164 ✅
- Can reach 172.133.x.x subnet: YES ✅

**Modbus Devices:**
- GH-3: 172.133.230.7:502 (Currently offline)
- GH-4: 172.133.0.149:502 (Currently offline)  
- GH-5: 172.133.0.149:502 (Currently offline)

**Database:**
- Server: (localdb)\MSSQLLocalDB ✅
- Database: HALHeatTreatmentCard ✅
- Table: FurnaceTemperatureSnapshots ✅
- Status: Ready and waiting for data
