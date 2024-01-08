namespace Ion.Storage;

public interface IPersistentStorage
{
	IPersistentStorageProvider Game { get; }
	IPersistentStorageProvider Assets { get; }

	IPersistentStorageProvider User { get; }
	IPersistentStorageProvider Saves { get; }
}