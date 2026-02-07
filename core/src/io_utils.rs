use crate::protocol::TransferManifest;
use std::fs::{self, File};
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

/// A reader that concatenates multiple sources (memory buffer + files) into a single stream.
/// Used for sending Manifest + File1 + File2... as one continuous stream.
pub struct MultiFileReader {
    /// Initial data (kept in memory, usually Manifest JSON)
    head_data: Vec<u8>,
    /// List of file paths to read sequentially
    file_paths: Vec<PathBuf>,
    /// Current position in the overall stream
    global_offset: u64,
    /// Total size (head_data.len() + sum(files.len()))
    total_size: u64,
    /// Cached file sizes to avoid repeated metadata calls
    file_sizes: Vec<u64>,
    /// Current open file handle (lazy loaded)
    current_file: Option<File>,
    /// Index of the file currently open in `file_paths`
    current_file_idx: Option<usize>,
}

impl MultiFileReader {
    pub fn new(head_data: Vec<u8>, file_paths: Vec<PathBuf>) -> io::Result<Self> {
        let mut file_sizes = Vec::with_capacity(file_paths.len());
        let mut total_file_size = 0;

        for path in &file_paths {
            let metadata = std::fs::metadata(path)?;
            let size = metadata.len();
            file_sizes.push(size);
            total_file_size += size;
        }

        Ok(Self {
            total_size: (head_data.len() as u64) + total_file_size,
            head_data,
            file_paths,
            file_sizes,
            global_offset: 0,
            current_file: None,
            current_file_idx: None,
        })
    }

    pub fn total_size(&self) -> u64 {
        self.total_size
    }
}

impl Read for MultiFileReader {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        let head_len = self.head_data.len() as u64;

        // 1. Read from Head Data (Manifest)
        if self.global_offset < head_len {
            let start = self.global_offset as usize;
            let available = head_len - self.global_offset;
            let to_read = std::cmp::min(buf.len() as u64, available) as usize;

            buf[..to_read].copy_from_slice(&self.head_data[start..start + to_read]);
            self.global_offset += to_read as u64;

            return Ok(to_read);
        }

        // 2. Read from Files
        let mut relative_offset = self.global_offset - head_len;

        // Find which file we are in
        let mut file_idx = 0;
        for &size in &self.file_sizes {
            if relative_offset < size {
                break;
            }
            relative_offset -= size;
            file_idx += 1;
        }

        // Check EOF
        if file_idx >= self.file_paths.len() {
            return Ok(0);
        }

        // Open file if needed or switch file
        if self.current_file_idx != Some(file_idx) {
            let path = &self.file_paths[file_idx];
            let mut file = File::open(path)?;

            // If we jumped into the middle of a file, seek
            if relative_offset > 0 {
                file.seek(SeekFrom::Start(relative_offset))?;
            }

            self.current_file = Some(file);
            self.current_file_idx = Some(file_idx);
        }

        // Read from current file
        let file = self.current_file.as_mut().unwrap();
        let n = file.read(buf)?;

        if n == 0 && relative_offset < self.file_sizes[file_idx] {
            return Err(io::Error::new(
                io::ErrorKind::UnexpectedEof,
                "File truncated during transfer",
            ));
        }

        self.global_offset += n as u64;
        Ok(n)
    }
}

impl Seek for MultiFileReader {
    fn seek(&mut self, pos: SeekFrom) -> io::Result<u64> {
        let new_offset = match pos {
            SeekFrom::Start(n) => n,
            SeekFrom::Current(n) => (self.global_offset as i64 + n) as u64,
            SeekFrom::End(n) => (self.total_size as i64 + n) as u64,
        };

        if new_offset > self.total_size {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "Seek beyond end of stream",
            ));
        }

        self.global_offset = new_offset;

        self.current_file_idx = None;
        self.current_file = None;

        Ok(self.global_offset)
    }
}

/// A writer that can handle either a single file or a folder stream (Manifest + Files)
pub enum ReceiverWriter {
    SingleFile(File),
    Folder {
        state: FolderWriterState,
        base_path: PathBuf,
        manifest_hash: Option<String>,
    },
}

pub enum FolderWriterState {
    ReadingManifestLen {
        buf: [u8; 4],
        filled: usize,
    },
    ReadingManifest {
        len: usize,
        buf: Vec<u8>,
    },
    WritingFiles {
        manifest: TransferManifest,
        current_file_idx: usize,
        current_offset_in_file: u64,
        current_file: Option<File>,
    },
    Error(String),
}

impl ReceiverWriter {
    pub fn new_single(path: &Path) -> io::Result<Self> {
        // Ensure parent directory exists (critical for Android Scoped Storage)
        if let Some(parent) = path.parent() {
            if !parent.as_os_str().is_empty() && !parent.exists() {
                fs::create_dir_all(parent)?;
            }
        }
        let file = File::create(path)?;
        Ok(ReceiverWriter::SingleFile(file))
    }

    pub fn new_folder(base_path: &Path) -> Self {
        ReceiverWriter::Folder {
            state: FolderWriterState::ReadingManifestLen {
                buf: [0u8; 4],
                filled: 0,
            },
            base_path: base_path.to_path_buf(),
            manifest_hash: None,
        }
    }

    /// Create for resume (Single File)
    pub fn resume_single(path: &Path, len: u64) -> io::Result<Self> {
        // Ensure parent directory exists
        if let Some(parent) = path.parent() {
            if !parent.as_os_str().is_empty() && !parent.exists() {
                fs::create_dir_all(parent)?;
            }
        }
        let mut file = std::fs::OpenOptions::new().write(true).open(path)?;
        file.set_len(len)?;
        file.seek(SeekFrom::Start(len))?;
        Ok(ReceiverWriter::SingleFile(file))
    }

    pub fn flush(&mut self) -> io::Result<()> {
        match self {
            ReceiverWriter::SingleFile(f) => f.flush(),
            ReceiverWriter::Folder { state, .. } => match state {
                FolderWriterState::WritingFiles { current_file, .. } => {
                    if let Some(f) = current_file {
                        f.flush()?;
                    }
                    Ok(())
                }
                _ => Ok(()),
            },
        }
    }

    pub fn manifest_checksum(&self) -> Option<String> {
        match self {
            ReceiverWriter::Folder { manifest_hash, .. } => manifest_hash.clone(),
            _ => None,
        }
    }
}

impl Write for ReceiverWriter {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        match self {
            ReceiverWriter::SingleFile(f) => f.write(buf),
            ReceiverWriter::Folder {
                state,
                base_path,
                manifest_hash,
            } => handle_folder_write(state, base_path, buf, manifest_hash),
        }
    }

    fn flush(&mut self) -> io::Result<()> {
        ReceiverWriter::flush(self)
    }
}

fn handle_folder_write(
    state: &mut FolderWriterState,
    base_path: &Path,
    buf: &[u8],
    manifest_hash_out: &mut Option<String>,
) -> io::Result<usize> {
    let mut buf_idx = 0;

    loop {
        if buf_idx >= buf.len() {
            return Ok(buf.len());
        }

        match state {
            FolderWriterState::ReadingManifestLen {
                buf: len_buf,
                filled,
            } => {
                let needed = 4 - *filled;
                let available = buf.len() - buf_idx;
                let to_copy = std::cmp::min(needed, available);

                len_buf[*filled..*filled + to_copy]
                    .copy_from_slice(&buf[buf_idx..buf_idx + to_copy]);
                *filled += to_copy;
                buf_idx += to_copy;

                if *filled == 4 {
                    let len = u32::from_be_bytes(*len_buf) as usize;
                    // Sanity check: Manifest shouldn't be massive (e.g. > 100MB)
                    if len > 100 * 1024 * 1024 {
                        return Err(io::Error::new(
                            io::ErrorKind::InvalidData,
                            "Manifest too large",
                        ));
                    }
                    *state = FolderWriterState::ReadingManifest {
                        len,
                        buf: Vec::with_capacity(len),
                    };
                }
            }
            FolderWriterState::ReadingManifest { len, buf: m_buf } => {
                let needed = *len - m_buf.len();
                let available = buf.len() - buf_idx;
                let to_copy = std::cmp::min(needed, available);

                m_buf.extend_from_slice(&buf[buf_idx..buf_idx + to_copy]);
                buf_idx += to_copy;

                if m_buf.len() == *len {
                    // Checksum the manifest bytes
                    *manifest_hash_out = Some(crate::checksum::calculate_chunk_checksum(m_buf));

                    // Parse Manifest
                    let manifest: TransferManifest =
                        serde_json::from_slice(m_buf).map_err(|e| {
                            io::Error::new(
                                io::ErrorKind::InvalidData,
                                format!("Invalid manifest: {}", e),
                            )
                        })?;

                    // Create directories
                    fs::create_dir_all(base_path)?;
                    for item in &manifest.items {
                        if let Some(parent) = Path::new(&item.path).parent() {
                            if !parent.as_os_str().is_empty() {
                                let dir_path = base_path.join(parent);
                                fs::create_dir_all(&dir_path)?;
                            }
                        }
                    }

                    *state = FolderWriterState::WritingFiles {
                        manifest,
                        current_file_idx: 0,
                        current_offset_in_file: 0,
                        current_file: None,
                    };
                }
            }
            FolderWriterState::WritingFiles {
                manifest,
                current_file_idx,
                current_offset_in_file,
                current_file,
            } => {
                if *current_file_idx >= manifest.items.len() {
                    // We received extra bytes? Or maybe strict match?
                    // Just consume loop
                    return Ok(buf.len());
                }

                let item = &manifest.items[*current_file_idx];
                let remaining_in_file = item.size - *current_offset_in_file;

                if remaining_in_file == 0 {
                    // Handle empty file creation
                    if item.size == 0 && *current_offset_in_file == 0 {
                        let path = base_path.join(&item.path);
                        File::create(&path)?;
                    }

                    // File complete (or empty file), move to next
                    *current_file_idx += 1;
                    *current_offset_in_file = 0;
                    *current_file = None; // Drop file handle
                    continue;
                }

                // Open file if not open
                if current_file.is_none() {
                    let path = base_path.join(&item.path);
                    // Use create (overwrite) for now
                    let f = File::create(&path)?;
                    *current_file = Some(f);
                }

                let available = buf.len() - buf_idx;
                let to_write = std::cmp::min(remaining_in_file, available as u64) as usize;

                if let Some(f) = current_file {
                    f.write_all(&buf[buf_idx..buf_idx + to_write])?;
                    // Flush if file is done
                    if *current_offset_in_file + (to_write as u64) == item.size {
                        f.flush()?;
                    }
                }

                *current_offset_in_file += to_write as u64;
                buf_idx += to_write;

                if *current_offset_in_file == item.size {
                    *current_file_idx += 1;
                    *current_offset_in_file = 0;
                    *current_file = None;
                }
            }
            FolderWriterState::Error(e) => {
                return Err(io::Error::other(e.clone()));
            }
        }
    }
}
