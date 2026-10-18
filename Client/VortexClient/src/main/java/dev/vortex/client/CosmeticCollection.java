package dev.vortex.client;

import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.atomic.AtomicLong;

/** User-wide, content-addressed collection. Imports never replace a previous design. */
public final class CosmeticCollection {
    public enum Kind { SKIN, CAPE }
    public record Entry(Kind kind,String id) { }
    public record Details(String name,boolean favorite) { }
    public static final int LIMIT=64;
    private static final AtomicLong REVISION=new AtomicLong();
    private final Path root;
    public CosmeticCollection(Path root){this.root=root.toAbsolutePath().normalize();}
    public static CosmeticCollection local(){return new CosmeticCollection(Path.of(System.getProperty("user.home"),".vortex","wardrobe"));}
    public static long revision(){return REVISION.get();}
    private static void unlinked(Path path) throws Exception {
        for(Path p=path;p!=null;p=p.getParent())if(Files.isSymbolicLink(p))throw new IllegalArgumentException("Linked collection folders are not supported.");
    }
    private Path detailsPath(Entry entry) throws Exception {
        Path file=path(entry).resolveSibling(entry.id()+".meta");unlinked(file);return file;
    }
    public Details details(Entry entry) throws Exception {
        Path file=detailsPath(entry);if(!Files.exists(file))return new Details("",false);
        if(Files.size(file)>512)throw new IllegalArgumentException("Collection label is too large.");
        try(var input=new java.io.DataInputStream(Files.newInputStream(file))) {
            if(input.readInt()!=0x56574D31)throw new IllegalArgumentException("Invalid collection label.");
            boolean favorite=input.readBoolean();String name=input.readUTF();
            if(input.read()!=-1 || !name.equals(cleanName(name)))throw new IllegalArgumentException("Invalid collection label.");
            return new Details(name,favorite);
        }
    }
    public static String cleanName(String name) {
        if(name==null)return "";
        var clean=new StringBuilder();name.strip().codePoints().filter(c->!Character.isISOControl(c) && c!=0x00A7).limit(40).forEach(clean::appendCodePoint);
        return clean.toString().strip();
    }
    public void describe(Entry entry,String name,boolean favorite) throws Exception {
        Path image=path(entry);if(!Files.isRegularFile(image,LinkOption.NOFOLLOW_LINKS))throw new IllegalArgumentException("Saved design is unavailable.");
        Path file=detailsPath(entry),temp=Files.createTempFile(file.getParent(),"label-",".part");
        try {
            try(var out=new java.io.DataOutputStream(Files.newOutputStream(temp))){out.writeInt(0x56574D31);out.writeBoolean(favorite);out.writeUTF(cleanName(name));}
            unlinked(file);
            try{Files.move(temp,file,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}
            catch(AtomicMoveNotSupportedException unsupported){Files.move(temp,file,StandardCopyOption.REPLACE_EXISTING);}
            REVISION.incrementAndGet();
        } finally {Files.deleteIfExists(temp);}
    }
    /** Recoverable removal: originals are moved, never overwritten or permanently erased. */
    public Path remove(Entry entry) throws Exception {
        Path image=path(entry),meta=detailsPath(entry),trash=root.resolve(".trash");unlinked(trash);
        if(!Files.isRegularFile(image,LinkOption.NOFOLLOW_LINKS))throw new IllegalArgumentException("Saved design is unavailable.");
        Files.createDirectories(trash);Path folder=Files.createDirectory(trash.resolve(UUID.randomUUID().toString()));
        Path backup=folder.resolve(image.getFileName());Files.move(image,backup);
        try {if(Files.exists(meta))Files.move(meta,folder.resolve(meta.getFileName()));}
        catch(Exception error){Files.move(backup,image);throw error;}
        REVISION.incrementAndGet();return backup;
    }
    private Path directory(Kind kind){return root.resolve(kind==Kind.SKIN?"skins":"capes");}
    private Path path(Entry entry) throws Exception {
        String pattern=entry.kind()==Kind.SKIN?"[a-f0-9]{64}":"(?:custom|hd)_[a-f0-9]{64}";
        if(!entry.id().matches(pattern))throw new IllegalArgumentException("Invalid collection entry.");
        Path path=directory(entry.kind()).resolve(entry.id()+(entry.kind()==Kind.SKIN?".skin":".cape"));
        for(Path p=path;p!=null;p=p.getParent())if(Files.isSymbolicLink(p))throw new IllegalArgumentException("Linked collection folders are not supported.");
        return path;
    }
    public List<Entry> entries(Kind kind) throws Exception {
        var result=new ArrayList<Entry>();Path dir=directory(kind);
        // Validate ancestors before traversing the directory, including existing links.
        path(new Entry(kind,kind==Kind.SKIN?"0".repeat(64):"custom_"+"0".repeat(64)));
        if(!Files.exists(dir))return List.of();
        try(var files=Files.newDirectoryStream(dir,kind==Kind.SKIN?"*.skin":"*.cape")) {
            for(Path file:files){
                String name=file.getFileName().toString();String id=name.substring(0,name.lastIndexOf('.'));
                if(!(kind==Kind.SKIN?id.matches("[a-f0-9]{64}"):id.matches("(?:custom|hd)_[a-f0-9]{64}")))continue;
                var entry=new Entry(kind,id);path(entry);
                if(!Files.isRegularFile(file,LinkOption.NOFOLLOW_LINKS))continue;
                result.add(entry);
                if(result.size()>LIMIT)throw new IllegalArgumentException("The wardrobe collection has too many files (maximum 64 per type).");
            }
        }
        result.sort(Comparator.comparing(Entry::id));return List.copyOf(result);
    }
    private void room(Entry entry) throws Exception {
        if(!Files.exists(path(entry)) && entries(entry.kind()).size()>=LIMIT)
            throw new IllegalArgumentException("Collection full: 64 designs per type. Existing images were kept.");
    }
    public Entry addSkin(SkinPixels.Selection skin) throws Exception {
        if(skin.rgba().length!=SkinPixels.SIZE)throw new IllegalArgumentException("Invalid skin pixels.");
        var entry=new Entry(Kind.SKIN,skinId(skin));room(entry);
        if(Files.exists(path(entry))){readSkin(entry);return entry;}
        SkinPixels.save(path(entry),new SkinPixels.Selection(true,skin.slim(),skin.rgba()));REVISION.incrementAndGet();return entry;
    }
    public static String skinId(SkinPixels.Selection skin) throws Exception {
        var hash=MessageDigest.getInstance("SHA-256");hash.update((byte)(skin.slim()?1:0));hash.update(skin.rgba());
        return HexFormat.of().formatHex(hash.digest());
    }
    public SkinPixels.Selection readSkin(Entry entry) throws Exception {
        if(entry.kind()!=Kind.SKIN)throw new IllegalArgumentException("Not a skin.");
        var skin=SkinPixels.load(path(entry));if(skin==null)throw new IllegalArgumentException("Saved skin is unavailable.");
        var hash=MessageDigest.getInstance("SHA-256");hash.update((byte)(skin.slim()?1:0));hash.update(skin.rgba());
        if(!entry.id().equals(HexFormat.of().formatHex(hash.digest())))throw new IllegalArgumentException("Saved skin checksum mismatch; original file kept.");
        return skin;
    }
    public Entry addCape(byte[] pixels) throws Exception {
        CapePixels.unpack(pixels);var entry=new Entry(Kind.CAPE,CapePixels.id(pixels).replace(':','_'));room(entry);
        if(Files.exists(path(entry))){readCape(entry);return entry;}
        CapePixels.save(path(entry),pixels);REVISION.incrementAndGet();return entry;
    }
    public byte[] readCape(Entry entry) throws Exception {
        if(entry.kind()!=Kind.CAPE)throw new IllegalArgumentException("Not a cape.");
        byte[] data=CapePixels.load(path(entry));
        if(!entry.id().equals(CapePixels.id(data).replace(':','_')))throw new IllegalArgumentException("Saved cape checksum mismatch; original file kept.");
        return data;
    }
}
