#ifndef voidwarp_core_h
#define voidwarp_core_h

#include <stdint.h>
#include <stdbool.h>

// Opaque Types
typedef struct VoidWarpHandle VoidWarpHandle;
typedef struct FfiTcpSender FfiTcpSender;
typedef struct FfiReceiver FfiReceiver;

// Structures
typedef struct {
    const char* device_id;
    const char* device_name;
    const char* ip_address;
    uint16_t port;
} FfiPeer;

typedef struct {
    const FfiPeer* peers;
    uintptr_t count;
} FfiPeerList;

typedef struct {
    const char* sender_name;
    const char* sender_addr;
    const char* file_name;
    uint64_t file_size;
    bool is_valid;
    bool is_folder;
} FfiPendingTransfer;

// Core Lifecycle
VoidWarpHandle* voidwarp_init(const char* device_name);
void voidwarp_destroy(VoidWarpHandle* handle);
const char* voidwarp_get_device_id(const VoidWarpHandle* handle);
const char* voidwarp_generate_pairing_code(void);
void voidwarp_free_string(char* s);

// Discovery
int32_t voidwarp_start_discovery(VoidWarpHandle* handle, uint16_t port);
int32_t voidwarp_start_discovery_with_ip(VoidWarpHandle* handle, uint16_t port, const char* ip_address);
void voidwarp_stop_discovery(VoidWarpHandle* handle);
FfiPeerList voidwarp_get_peers(VoidWarpHandle* handle);
void voidwarp_free_peer_list(FfiPeerList list);
int32_t voidwarp_add_manual_peer(VoidWarpHandle* handle, const char* device_id, const char* device_name, const char* ip_address, uint16_t port);

// TCP Sender
FfiTcpSender* voidwarp_tcp_sender_create(const char* file_path);
int32_t voidwarp_tcp_sender_start(FfiTcpSender* sender, const char* ip_address, uint16_t port, const char* sender_name);
const char* voidwarp_tcp_sender_get_checksum(FfiTcpSender* sender);
uint64_t voidwarp_tcp_sender_get_file_size(FfiTcpSender* sender);
float voidwarp_tcp_sender_get_progress(FfiTcpSender* sender);
void voidwarp_tcp_sender_cancel(FfiTcpSender* sender);
bool voidwarp_tcp_sender_is_folder(FfiTcpSender* sender);
// Performance Optimization: Streaming Mode
void voidwarp_tcp_sender_set_chunk_size(FfiTcpSender* sender, uintptr_t size);
void voidwarp_tcp_sender_destroy(FfiTcpSender* sender);

// File Receiver
FfiReceiver* voidwarp_create_receiver(void);
uint16_t voidwarp_receiver_get_port(FfiReceiver* receiver);
void voidwarp_receiver_start(FfiReceiver* receiver);
void voidwarp_receiver_stop(FfiReceiver* receiver);
// State: 0=Idle, 1=Listening, 2=AwaitingAccept, 3=Receiving, 4=Completed, 5=Error
int32_t voidwarp_receiver_get_state(FfiReceiver* receiver);
FfiPendingTransfer voidwarp_receiver_get_pending(FfiReceiver* receiver);
void voidwarp_free_pending_transfer(FfiPendingTransfer transfer);
int32_t voidwarp_receiver_accept(FfiReceiver* receiver, const char* save_path);
int32_t voidwarp_receiver_reject(FfiReceiver* receiver);
float voidwarp_receiver_get_progress(FfiReceiver* receiver);
uint64_t voidwarp_receiver_get_bytes_received(FfiReceiver* receiver);
void voidwarp_destroy_receiver(FfiReceiver* receiver);

// Transport Utilities
bool voidwarp_transport_start_server(uint16_t port);
bool voidwarp_transport_ping(const char* ip_address, uint16_t port);

#endif /* voidwarp_core_h */
