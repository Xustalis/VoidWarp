#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Script to insert multi-file methods into MainViewModel.cs after SelectFile method.
"""
import codecs

def insert_methods():
    main_file = r'G:\project\VoidWarp\platforms\windows\ViewModels\MainViewModel.cs'
    methods_file = r'G:\project\VoidWarp\platforms\windows\ViewModels\MainViewModel_MultiFile_Methods.txt'
    
    # Read both files
    with codecs.open(main_file, 'r', 'utf-8-sig') as f:
        main_content = f.read()
    
    with codecs.open(methods_file, 'r', 'utf-8-sig') as f:
        methods_content = f.read()
    
    # Clean up methods content - remove comment header
    methods_content = methods_content.replace(
        '// Multi-file queue management methods for MainViewModel.cs\r\n', ''
    ).replace(
        '// Insert these methods after the SelectFile() method\r\n', ''
    ).replace(
        '// Multi-file queue management methods for MainViewModel.cs\n', ''
    ).replace(
        '// Insert these methods after the SelectFile() method\n', ''
    )
    
    # Remove leading blank lines
    while methods_content.startswith('\r\n') or methods_content.startswith('\n'):
        methods_content = methods_content[2:] if methods_content.startswith('\r\n') else methods_content[1:]
    
    # Find the insertion point - after the closing brace of SelectFile method
    # Look for "private void ClearLogs()" as marker
    marker = '        private void ClearLogs()'
    insertion_point = main_content.find(marker)
    
    if insertion_point == -1:
        print("ERROR: Could not find insertion marker")
        return False
    
    # Insert methods before ClearLogs
    new_content = (
        main_content[:insertion_point] +
        methods_content +
        '\r\n\r\n' +
        main_content[insertion_point:]
    )
    
    # Write back
    with codecs.open(main_file, 'w', 'utf-8-sig') as f:
        f.write(new_content)
    
    print(f"Successfully inserted methods at position {insertion_point}")
    return True

if __name__ == '__main__':
    success = insert_methods()
    exit(0 if success else 1)
