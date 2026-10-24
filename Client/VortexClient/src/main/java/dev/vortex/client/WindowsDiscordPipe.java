package dev.vortex.client;

import com.sun.jna.*;
import com.sun.jna.ptr.IntByReference;
import com.sun.jna.win32.StdCallLibrary;
import java.io.*;
import java.net.SocketTimeoutException;
import java.util.*;

/** Overlapped Win32 IPC using Minecraft's existing JNA, never a synchronous file handle. */
final class WindowsDiscordPipe implements DiscordRpc.Transport {
    interface Api extends StdCallLibrary {
        Pointer CreateFileW(WString name,int access,int share,Pointer security,int disposition,int flags,Pointer template);
        Pointer CreateEventW(Pointer security,boolean manualReset,boolean initial,WString name);
        boolean ReadFile(Pointer handle,Pointer buffer,int length,IntByReference received,Pointer overlapped);
        boolean WriteFile(Pointer handle,Pointer buffer,int length,IntByReference sent,Pointer overlapped);
        boolean GetOverlappedResult(Pointer handle,Pointer overlapped,IntByReference transferred,boolean wait);
        boolean CancelIoEx(Pointer handle,Pointer overlapped);
        int WaitForSingleObject(Pointer handle,int milliseconds);
        boolean CloseHandle(Pointer handle);
        int GetLastError();
    }
    private static final class NativeApi { static final Api VALUE = Native.load("kernel32",Api.class); }
    @Structure.FieldOrder({"internal","internalHigh","offset","offsetHigh","event"})
    public static final class Overlapped extends Structure {
        public Pointer internal,internalHigh;
        public int offset,offsetHigh;
        public Pointer event;
    }
    private final Api api;
    private final Pointer handle;
    private final Object gate = new Object();
    private final Set<Overlapped> active = new HashSet<>();
    private boolean closed;
    WindowsDiscordPipe(String path) throws IOException {
        api = NativeApi.VALUE;
        handle = api.CreateFileW(new WString(path),0xC0000000,0,null,3,0x40000000,null);
        long value = handle == null ? 0 : Pointer.nativeValue(handle);
        if (value == 0 || value == -1 || value == 0xffffffffL) throw error("open",api.GetLastError());
    }
    private static IOException error(String operation,int code) { return new IOException("Discord IPC " + operation + " failed (Windows " + code + ")"); }
    @Override public int read(byte[] target,int offset,int length) throws IOException {
        Objects.checkFromIndexSize(offset,length,target.length);
        return transfer(false,target,offset,length);
    }
    @Override public void write(byte[] frame) throws IOException {
        if (transfer(true,frame,0,frame.length) != frame.length) throw new IOException("Incomplete Discord IPC write");
    }
    private int transfer(boolean writing,byte[] bytes,int offset,int length) throws IOException {
        if (length == 0) return 0;
        var operation = new Overlapped();
        operation.event = api.CreateEventW(null,true,false,null);
        if (operation.event == null) throw error("create event",api.GetLastError());
        operation.write();
        boolean registered = false, initiated = false, completed = false;
        try (var buffer = new Memory(length)) {
            if (writing) buffer.write(0,bytes,offset,length);
            var count = new IntByReference();
            try {
                synchronized (gate) {
                    if (closed) throw new EOFException("Discord IPC closed");
                    active.add(operation); registered = true;
                    boolean immediate = writing ? api.WriteFile(handle,buffer,length,count,operation.getPointer()) : api.ReadFile(handle,buffer,length,count,operation.getPointer());
                    int code = immediate ? 0 : api.GetLastError();
                    if (!immediate && code != 997) throw error(writing ? "write" : "read",code); // ERROR_IO_PENDING
                    initiated = true;
                }
                if (writing) {
                    int wait = api.WaitForSingleObject(operation.event,5000);
                    if (wait == 258) throw new SocketTimeoutException("Discord IPC write timed out");
                    if (wait != 0) throw error("wait",api.GetLastError());
                }
                boolean ok = api.GetOverlappedResult(handle,operation.getPointer(),count,true);
                completed = true;
                if (!ok) throw error(writing ? "write completion" : "read completion",api.GetLastError());
                int transferred = count.getValue();
                if (transferred < 0 || transferred > length) throw new IOException("Invalid Discord IPC byte count");
                if (!writing && transferred > 0) buffer.read(0,bytes,offset,transferred);
                return !writing && transferred == 0 ? -1 : transferred;
            } finally {
                // Native buffers and OVERLAPPED must remain alive until cancellation completes.
                if (initiated && !completed) {
                    api.CancelIoEx(handle,operation.getPointer());
                    api.GetOverlappedResult(handle,operation.getPointer(),count,true);
                }
            }
        } finally {
            synchronized (gate) {
                if (registered) { active.remove(operation); if (closed && active.isEmpty()) api.CloseHandle(handle); }
            }
            api.CloseHandle(operation.event);
        }
    }
    @Override public void close() {
        synchronized (gate) {
            if (closed) return;
            closed = true;
            if (active.isEmpty()) api.CloseHandle(handle);
            else api.CancelIoEx(handle,null); // Final completing operation releases the handle.
        }
    }
}
