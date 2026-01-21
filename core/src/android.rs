#![cfg(target_os = "android")]
#![allow(non_snake_case)]

use jni::JNIEnv;
use jni::objects::{JClass, JString, JObject, JValue};
use jni::sys::{jlong, jint, jstring, jobjectArray, jboolean};
use std::ffi::{CString, CStr};
use crate::ffi;

/// Convert JString to CString
fn get_string(env: &mut JNIEnv, string: JString) -> CString {
    let input: String = env.get_string(&string)
        .expect("Couldn't get java string!")
        .into();
    CString::new(input).unwrap_or_default()
}

/// Convert *mut c_char to JString
unsafe fn from_c_string(env: &mut JNIEnv, ptr: *mut std::os::raw::c_char) -> jstring {
    if ptr.is_null() {
        return JObject::null().into_raw();
    }
    let c_str = CStr::from_ptr(ptr);
    let s = c_str.to_string_lossy();
    // We must free the string if it was allocated by voidwarp functions
    // But here we just convert. The caller handles freeing via voidwarp_free_string if needed.
    // Wait, voidwarp_get_device_id returns a new malloc'd string that MUST be freed.
    // So we should free it after converting.
    let output = env.new_string(&*s).expect("Couldn't create java string!");
    output.into_raw()
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpInit(
    mut env: JNIEnv,
    _class: JClass,
    device_name: JString,
) -> jlong {
    let name = get_string(&mut env, device_name);
    let handle = ffi::voidwarp_init(name.as_ptr());
    handle as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpDestroy(
    _env: JNIEnv,
    _class: JClass,
    handle: jlong,
) {
    ffi::voidwarp_destroy(handle as *mut ffi::VoidWarpHandle);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGetDeviceId(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
) -> jstring {
    let ptr = ffi::voidwarp_get_device_id(handle as *const ffi::VoidWarpHandle);
    let jstr = from_c_string(&mut env, ptr);
    ffi::voidwarp_free_string(ptr); // Free the Rust-allocated string
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGeneratePairingCode(
    mut env: JNIEnv,
    _class: JClass,
) -> jstring {
    let ptr = ffi::voidwarp_generate_pairing_code();
    let jstr = from_c_string(&mut env, ptr);
    ffi::voidwarp_free_string(ptr);
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpStartDiscovery(
    _env: JNIEnv,
    _class: JClass,
    handle: jlong,
    port: jint,
) -> jint {
    ffi::voidwarp_start_discovery(handle as *mut ffi::VoidWarpHandle, port as u16)
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpStopDiscovery(
    _env: JNIEnv,
    _class: JClass,
    handle: jlong,
) {
    ffi::voidwarp_stop_discovery(handle as *mut ffi::VoidWarpHandle);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpGetPeers(
    mut env: JNIEnv,
    _class: JClass,
    handle: jlong,
) -> jobjectArray {
    let list = ffi::voidwarp_get_peers(handle as *const ffi::VoidWarpHandle);
    
    // Create PeerInfo array
    let peer_class = env.find_class("com/voidwarp/android/native/NativeLib$PeerInfo")
        .expect("Could not find PeerInfo class");
    
    let initial_element = JObject::null();
    let output_array = env.new_object_array(list.count as i32, &peer_class, &initial_element)
        .expect("Could not create array");

    if list.count > 0 && !list.peers.is_null() {
        let peers_slice = std::slice::from_raw_parts(list.peers, list.count);
        for (i, peer) in peers_slice.iter().enumerate() {
            let id = CStr::from_ptr(peer.device_id).to_string_lossy();
            let name = CStr::from_ptr(peer.device_name).to_string_lossy();
            let ip = CStr::from_ptr(peer.ip_address).to_string_lossy();
            
            let j_id = env.new_string(&*id).unwrap();
            let j_name = env.new_string(&*name).unwrap();
            let j_ip = env.new_string(&*ip).unwrap();
            
            // Constructor: (String, String, String, Int)
            let obj = env.new_object(
                &peer_class,
                "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)V",
                &[
                    JValue::Object(&j_id),
                    JValue::Object(&j_name),
                    JValue::Object(&j_ip),
                    JValue::Int(peer.port as i32)
                ]
            ).expect("Failed to create PeerInfo object");
            
            env.set_object_array_element(&output_array, i as i32, &obj)
                .expect("Failed to set array element");
        }
    }

    ffi::voidwarp_free_peer_list(list);
    output_array.into_raw()
}

// File Transfer bindings
#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpCreateSender(
    mut env: JNIEnv,
    _class: JClass,
    path: JString,
) -> jlong {
    let path_c = get_string(&mut env, path);
    let sender = ffi::voidwarp_create_sender(path_c.as_ptr());
    sender as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetSize(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jlong {
    ffi::voidwarp_sender_get_size(sender as *const ffi::FfiFileSender) as jlong
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetName(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> jstring {
    let ptr = ffi::voidwarp_sender_get_name(sender as *const ffi::FfiFileSender);
    let jstr = from_c_string(&mut env, ptr);
    // name is a new CString, so we must free it?
    // ffi source: CString::new(...).into_raw(). Caller must free.
    // Yes, we must free. BUT ffi doesn't expose voidwarp_free_string from generic pointer?
    // It's just voidwarp_free_string(s: *mut c_char).
    ffi::voidwarp_free_string(ptr);
    jstr
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderGetProgress(
    mut env: JNIEnv,
    _class: JClass,
    sender: jlong,
) -> JObject<'static> {
    let progress = ffi::voidwarp_sender_get_progress(sender as *const ffi::FfiFileSender);
    
    let progress_class = env.find_class("com/voidwarp/android/native/NativeLib$TransferProgress")
        .expect("Could not find TransferProgress class");
        
    // Constructor: (Long, Long, Float, Float, Int)
    let obj = env.new_object(
        &progress_class,
        "(JJFFI)V",
        &[
            JValue::Long(progress.bytes_transferred as i64),
            JValue::Long(progress.total_bytes as i64),
            JValue::Float(progress.percentage),
            JValue::Float(progress.speed_mbps),
            JValue::Int(progress.state)
        ]
    ).expect("Failed to create TransferProgress object");
    
    obj
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpSenderCancel(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    ffi::voidwarp_sender_cancel(sender as *mut ffi::FfiFileSender);
}

#[no_mangle]
pub unsafe extern "C" fn Java_com_voidwarp_android_native_NativeLib_voidwarpDestroySender(
    _env: JNIEnv,
    _class: JClass,
    sender: jlong,
) {
    ffi::voidwarp_destroy_sender(sender as *mut ffi::FfiFileSender);
}
